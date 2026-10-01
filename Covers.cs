using System.Net.Http;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MyAudiobooks;

/// De omslagen.
///
/// Ze komen van de server en worden onderweg gedownload, in het geheugen
/// gezet en daarna nooit meer opnieuw gevraagd. Twee dingen doen ertoe:
///
///  - **Het downloaden gebeurt naast het tekenen, niet ervoor.** Een rij
///    omslagen bouwen wacht niet op het netwerk; het netwerk vult ze later aan.
///    De andere kant daarvan is dat het scherm leeg begint en vol loopt, en dat
///    is beter dan een scherm dat twintig seconden niets toont en dan opeens
///    alles.
///
///  - **Het geheugen is begrensd.** Een omslag van 300 bij 300 pixels is een
///    kwart megabyte in het geheugen. Een collectie van duizend boeken is daar
///    ruim een gigabyte aan, en dat is een programma dat na een middag luisteren
///    de helft van het werkgeheugen heeft. Dus: kleiner dan de omslag is
///    (200 pixels breed is meer dan genoeg voor een kaartje van 150), en
///    hoogstens een paar honderd onthouden. Wat eruit valt wordt de volgende
///    keer gewoon opnieuw gehaald.
public sealed class Covers
{
    private const int Wide = 200;
    private const int Max = 160;

    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly Dictionary<long, ImageSource> _have = new();
    private readonly Queue<long> _order = new();
    private readonly HashSet<long> _busy = new();
    private readonly object _gate = new();

    public Covers(HttpClient http, string baseUrl)
    {
        _http = http;
        _baseUrl = baseUrl;
    }

    public void Drop(long bookId)
    {
        lock (_gate) { _have.Remove(bookId); }
    }

    /// Zet een omslag in een vak. Komt hij niet op scherm, dan blijft het vak
    /// leeg en wordt er niets getekend — een gebroken plaatje met een
    /// kruisje erin is erger dan een leeg vak, want het suggereert dat er een
    /// omslag is en alleen het laden mislukte.
    public void Put(Image box, long bookId, string? cover, int version, CancellationToken stop)
    {
        if (string.IsNullOrWhiteSpace(cover))
        {
            box.Source = null;
            return;
        }

        lock (_gate)
        {
            if (_have.TryGetValue(bookId, out var known))
            {
                box.Source = known;
                return;
            }
            if (!_busy.Add(bookId))
            {
                // al onderweg; de eerste aanroep zet hem er straks in
                return;
            }
        }

        var url = $"{_baseUrl}/api/cover/{bookId}?v={version}";
        _ = Task.Run(async () =>
        {
            try
            {
                var bytes = await _http.GetByteArrayAsync(url, stop);
                if (bytes.Length == 0) return;

                var image = new BitmapImage();
                using (var stream = new MemoryStream(bytes, writable: false))
                {
                    // OnLoad: het beeld wordt meteen uit de stroom gelezen, want de
                    // stroom is een stukje later weg en dan zou dit vak leeg
                    // blijven zonder dat er iets te zien is.
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.DecodePixelWidth = Wide;
                    image.StreamSource = stream;
                    image.EndInit();
                }
                image.Freeze(); // vanaf hier mag het van elke draad

                lock (_gate)
                {
                    _have[bookId] = image;
                    _order.Enqueue(bookId);
                    while (_order.Count > Max)
                    {
                        if (_have.Remove(_order.Dequeue())) { /* weggezet, en bij een volgende blik komt het opnieuw */ }
                    }
                }

                box.Dispatcher.Invoke(() => box.Source = image, DispatcherPriority.Background);
            }
            catch (Exception e) when (e is HttpRequestException or IOException or NotSupportedException
                                      or System.Runtime.InteropServices.COMException or OperationCanceledException)
            {
                // Geen omslag, of de server gaf iets dat geen plaatje is. In
                // beide gevallen blijft het vak leeg. Dat is zichtbaar (er is
                // geen plaatje) en het zegt wat er mis is zonder dat er iets
                // mis hoeft te gaan om het te zien.
            }
            finally
            {
                lock (_gate) _busy.Remove(bookId);
            }
        }, stop);
    }

    /// Het adres van een omslag, voor wie er niets mee hoeft te doen dan hem
    /// gebruiken.
    public Uri? Of(long bookId, string? cover, int version) =>
        string.IsNullOrWhiteSpace(cover) ? null : new Uri($"{_baseUrl}/api/cover/{bookId}?v={version}");
}
