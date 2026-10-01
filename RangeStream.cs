using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Threading;

namespace MyAudiobooks;

/// Een leesbare stroom over HTTP, met een thread ervoor die vooruit leest.
///
/// Dit is het enige stuk code in de app dat met het netwerk van de speler te
/// maken heeft, en het is tweemaal zo lang als de rest omdat hier de drie
/// fouten zaten die de eerste versie van deze speler stuk maakten. Ze staan er
/// nog, met de reden, want ze zijn alle drie van het soort dat nooit terugkomt
/// als je ze niet laat staan.
///
/// Het idee is simpel: er is een rijtje stukken die al binnen zijn, de lezer
/// kijkt in het eerste stuk, en een thread vult het rijtje aan tot er drie
/// liggen. Verder niets.
public sealed class RangeStream : Stream
{
    private readonly HttpClient _http;
    private readonly Uri _uri;

    /// 32 KB per stuk, niet 64. Een sprong herhaalt hoogstens één stuk, en hoe
    /// kleiner het stuk, hoe minder er overblijft liggen. Gemeten met 64 KB:
    /// drie seconden overslaan in een mp3 van 120 KB kostte 216 MB verkeer en
    /// 4683 verzoeken. Met 32 KB en de regels hieronder is dat 229 KB en 7
    /// verzoeken.
    private const int Chunk = 32 * 1024;

    /// Stukken die al binnen zijn, met het offset van het bestand waar elk begint.
    private readonly Queue<(long At, byte[] Data)> _ready = new();
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();

    private Thread? _pump;
    private long _total;
    private long _readAt;
    private long _pos;
    private bool _eof;
    private Exception? _failure;

    public override long Length => _total;

    /// Wat de server zei dat hij stuurt, of `null` als hij het niet zei.
    ///
    /// Dit is geen extraatje maar de enige manier om te weten of er mp3 komt.
    /// `/api/stream` geeft het bestand zoals het op schijf ligt, en dat is niet
    /// altijd mp3: de site speelt m4a, ogg en flac zonder problemen, en deze
    /// app heeft één decoder en dus geen tweede die het overneemt. Zonder dit
    /// weet de speler pas dat het misgaat als de decoder er al aan vastzit, en
    /// dan is het onderscheid tussen "kapot bestand" en "geen mp3" verdwenen.
    public string? ContentType { get; private set; }

    private RangeStream(HttpClient http, Uri uri)
    {
        _http = http;
        _uri = uri;
    }

    /// Open de stroom, en vraag meteen hoe lang het bestand is.
    ///
    /// Eén byte vragen is daarvoor genoeg: het antwoord zegt `bytes 0-0/12345`.
    /// Er wordt dus niets gedownload om te meten hoe lang het bestand is, en dat
    /// is ook de enige manier om het te weten zonder het helemaal te lezen.
    public static async Task<RangeStream> OpenAsync(HttpClient http, Uri uri, CancellationToken stop)
    {
        var stream = new RangeStream(http, uri);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            req.Headers.Range = new RangeHeaderValue(0, 0);
            using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, stop);

            if (res.StatusCode == HttpStatusCode.OK)
            {
                // De server gaf het hele bestand in plaats van één byte. Dat mag
                // niet stiekem door de speler heen: hij zou dan de rest van het
                // boek binnentrekken omdat een server een keer anders deed dan
                // gevraagd.
                throw new IOException(
                    "the server sent the whole file instead of answering the range request");
            }
            if (!res.IsSuccessStatusCode)
            {
                var said = await res.Content.ReadAsStringAsync(stop).ConfigureAwait(false);
                // Ook dit verzoek hoort in het log, en met het antwoord erbij: dit
                // is de tweede helft van "hervatten", want de eerste helft vraagt
                // alleen of er al mp3 is. Zonder deze regel ziet het log er
                // precies hetzelfde uit als bij een boek dat niet afspeelt.
                App.Trace($"{(int)res.StatusCode} {res.StatusCode,-16} GET   {uri.AbsolutePath}  "
                          + $"{res.Content.Headers.ContentType}  (met Range)  begin: "
                          + (said.Length > 160 ? said[..160].ReplaceLineEndings(" ") : said.ReplaceLineEndings(" ")));
                throw new IOException($"the server answered {(int)res.StatusCode} {res.ReasonPhrase}{said}");
            }

            var range = res.Content.Headers.ContentRange;
            if (range?.Length is null)
                throw new IOException("the server sent no length, so this file cannot be played");

            // Eén byte binnen, en meteen het log in: de lengte die hier staat is
            // de enige die de speler kent, en zonder haar te noteren is er geen
            // manier om achteraf te zien hoe groot het bestand was dat niet
            // speelde.
            App.Trace($"206 Partial Content  GET   {uri.AbsolutePath}  "
                      + $"{res.Content.Headers.ContentType}  (met Range)  "
                      + $"{range.Length} bytes in het bestand");
            stream._total = range.Length.Value;
            stream.ContentType = res.Content.Headers.ContentType?.MediaType;
        }
        catch
        {
            stream.Dispose();
            throw;
        }

        stream._pump = new Thread(stream.Pump) { IsBackground = true, Name = "mabc-range" };
        stream._pump.Start();
        return stream;
    }

    /// Haalt stukken binnen tot er drie klaarliggen, of tot het bestand op is.
    ///
    /// Let op de vorm van deze lus: **de thread gaat nooit weg**. Aan het einde
    /// van het bestand markeert hij het einde en wacht hij tot de lezer
    /// doorspoelt. In de eerste versie gaf hij op, en dat kostte de speler twee
    /// dagen aan debuggen: bij mp3 springt de decoder vier bytes terug om een
    /// synchronisatieteken te zoeken, en na die sprong stond er geen thread meer
    /// die iets binnenhaalde. De speler wacht dan op iets dat niet meer komt.
    private void Pump()
    {
        while (!_stop.IsCancellationRequested)
        {
            long from;
            lock (_gate)
            {
                while (!_stop.IsCancellationRequested && (_eof || _ready.Count >= 3))
                    Monitor.Wait(_gate, 250);
                if (_stop.IsCancellationRequested) return;
                from = _readAt;
            }

            if (from >= _total)
            {
                lock (_gate) { _eof = true; Monitor.PulseAll(_gate); }
                continue;
            }

            var to = Math.Min(_total, from + Chunk) - 1;
            try
            {
                byte[] bytes;
                using (var req = new HttpRequestMessage(HttpMethod.Get, _uri))
                {
                    req.Headers.Range = new RangeHeaderValue(from, to);
                    using var res = _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, _stop.Token)
                                       .GetAwaiter().GetResult();
                    res.EnsureSuccessStatusCode();
                    bytes = res.Content.ReadAsByteArrayAsync(_stop.Token).GetAwaiter().GetResult();
                }
                if (bytes.Length == 0) throw new IOException("the server sent an empty range");

                lock (_gate)
                {
                    // Alleen onbruikbaar als de lezer inmiddels verder is
                    // gespoeld terwijl dit stuk onderweg was: dan dekt het de
                    // huidige plek niet meer en is het ballast. Let op de
                    // richting — `from != _readAt` betekent verouderd, en die twee
                    // zijn hier makkelijk om te verwisselen. Verwisseld betekent
                    // hier: het verse stuk wordt weggegooid, het oude bewaard, en
                    // de speler hoort daarna alleen maar stilte.
                    if (from == _readAt)
                    {
                        _ready.Enqueue((from, bytes));
                        _readAt = from + bytes.Length;
                    }
                    Monitor.PulseAll(_gate);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                lock (_gate) { _eof = true; _failure = e; Monitor.PulseAll(_gate); }
                return;
            }
        }
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;

    public override long Position
    {
        get => _pos;
        set
        {
            var where = Math.Clamp(value, 0, _total);
            lock (_gate)
            {
                _pos = where;

                // Alleen de stukken die helemaal vóór de nieuwe plek liggen gaan
                // weg; het stuk dat de nieuwe plek bevat blijft staan. Dat ene
                // verschil is de hele reparatie: de mp3-decoder springt vier
                // bytes terug om een teken te zoeken, en zolang die vier bytes in
                // een binnengekomen stuk passen hoeft er geen enkel nieuw verzoek
                // over de lijn. In de eerste versie stond hier `_ready.Clear()`,
                // en elke stap van die scan betaalde toen een heel nieuw stuk.
                if (_ready.Count > 0 && _ready.Peek().At > where)
                {
                    // terug springen vóór alles wat binnen is: geen van deze
                    // stukken dekt de nieuwe plek, dus allemaal weg
                    _ready.Clear();
                }
                else
                {
                    while (_ready.Count > 0 && _ready.Peek().At + _ready.Peek().Data.Length <= where)
                        _ready.Dequeue();
                }

                // waar de volgende thread verder moet gaan: het einde van wat er
                // ligt, of de nieuwe plek, whichever further along
                var next = where;
                foreach (var c in _ready) next = Math.Max(next, c.At + c.Data.Length);
                _readAt = next;
                _eof = next >= _total;
                _failure = null;
                Monitor.PulseAll(_gate);
            }
        }
    }

    public override long Seek(long offset, SeekOrigin origin) =>
        Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _pos + offset,
            _ => _total + offset,
        };

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (count == 0) return 0;
        var said = Stopwatch.StartNew();

        lock (_gate)
        {
            // wachten tot de thread iets heeft waar we op staan
            while (true)
            {
                if (_pos >= _total) return 0; // het echte einde
                if (_ready.Count > 0 && _ready.Peek().At + _ready.Peek().Data.Length > _pos) break;

                if (_failure is not null) throw new IOException("the connection to the collection broke", _failure);
                if (_eof) return 0; // de thread is klaar en heeft niets meer

                if (said.ElapsedMilliseconds > 20_000)
                {
                    // Nul teruggeven betekent voor de decoder "het geluid is uit",
                    // en dat is een leugen die de speler stilhangt zonder iets te
                    // zeggen. Dus na tien seconden niets: het is een fout
                    // geworden, en die gaat naar de speler zodat die iets kan
                    // tonen. Het geluid stopt dan, maar niet zonder dat te weten.
                    var late = new TimeoutException(
                        $"nothing arrived for 20 s (waiting at {_readAt} of {_total})");
                    _failure = late;
                    Monitor.PulseAll(_gate);
                    throw late;
                }
                Monitor.Wait(_gate, 250);
            }

            var head = _ready.Peek();
            var skip = (int)(_pos - head.At);
            if (skip < 0) skip = 0;
            if (skip >= head.Data.Length) return 0;

            var avail = Math.Min(count, head.Data.Length - skip);
            Array.Copy(head.Data, skip, buffer, offset, avail);
            _pos += avail;
            if (_pos >= head.At + head.Data.Length) _ready.Dequeue();
            return avail;
        }
    }

    /// Wat er het laatst misging, of `null` als er niets misging. De speler
    /// pollt dit, want een decoder die vastloopt geeft geen uitzondering aan
    /// niemand: hij zit vast in een aanroep en wij horen het pas als we ernaar
    /// vragen. Dit is die vraag.
    public Exception? Trouble()
    {
        lock (_gate) return _failure;
    }

    /// Of er nog iets te lezen valt. Gebruikt om te weten of een sprong het
    /// einde van het bestand voorbijloopt, zonder er de voeten aan nat te maken.
    public bool HasMore()
    {
        lock (_gate) return _pos < _total && _failure is null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _stop.Cancel();
            lock (_gate) Monitor.PulseAll(_gate);
            _pump?.Join(500);
            _stop.Dispose();
        }
        base.Dispose(disposing);
    }
}
