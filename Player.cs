using System.Diagnostics;
using System.Threading;
using NAudio.Wave;

namespace MyAudiobooks;

/// Wat er in de speler gebeurt waarvan het scherm het moet weten.
public enum Waiting
{
    /// Niet aan het wachten.
    None,

    /// De server maakt dit deel van mp3. `Done` en `Total` zijn seconden.
    Converting,
}

/// De speler: één boek, één deel, één geluid.
///
/// Drie dingen bepalen hoe dit is opgebouwd.
///
/// **Er is één decoder.** Alleen mp3, en dat komt van de server; zie
/// `ONDERZOEK-AUDIO.md`. Er is dus geen tweede decoder die half werkt, en geen
/// Media Foundation als reserve die soms meedoet. Wat niet mp3 is, zegt dit
/// wat het is.
///
/// **Er wordt nooit vanzelf begonnen.** `Load` zet de speler klaar, `Play`
/// begint. Die twee zijn aparte dingen, en de app roept ze nooit in één adem
/// aan: een programma dat je openzet en dat dan zelf het geluid aanzet, is een
/// programma dat begint te praten terwijl je nog niets hebt gedaan.
///
/// **Alles wat misgaat zegt wat.** Een decoder die vastloopt geeft geen
/// uitzondering aan niemand — hij zit vast in een aanroep. Daarom loopt er een
/// eigen klok die meeluistert, en daarom is er een `Trouble` op de stroom. Het
/// scherm hoeft niet te gokken waarom het stil is; het wordt het verteld.
public sealed class Player : IDisposable
{
    private readonly Collection _collection;
    private readonly Store _store;
    private readonly System.Threading.CancellationTokenSource _stop = new();

    private WaveOut? _out;
    private Mp3FileReaderBase? _reader;
    private RangeStream? _stream;

    /// Zegt wat er in het scherm veranderd is: tijd, deel, volume, aan of uit.
    public event Action? Changed;

    /// Iets wat iemand moet lezen. Een reden, geen toonval: het is het enige wat
    /// deze app zegt als er niets gebeurt, en daarom is het belangrijk dat er
    /// altijd iets is.
    public event Action<string>? Announced;

    /// Dit deel is uit. Of er een volgend is, weet alleen de speler.
    public event Action? PartEnded;

    public BookDetail? Book { get; private set; }
    public int Part { get; private set; }
    public bool IsPlaying { get; private set; }
    public Waiting Waiting { get; private set; } = Waiting.None;
    public double ConvertingDone { get; private set; }
    public double ConvertingTotal { get; private set; }

    private double _volume = 1;
    private bool _muted;
    private System.Threading.Timer? _clock;
    private bool _sleeping;
    private DateTimeOffset _sleepUntil;
    private int _lastSavedSecond = -1;
    private bool _disposed;

    public Player(Collection collection, Store store)
    {
        _collection = collection;
        _store = store;
        _volume = store.Volume;
        _muted = store.Muted;
        // 250 ms is snel genoeg dat de tijd meebeweegt zoals geluid, en traag
        // genoeg dat de klok zelf geen merkbare last is. Vier keer per seconde is
        // het verschil tussen "de tijd loopt mee" en "de tijd schokt".
        _clock = new System.Threading.Timer(Tick, null, 250, 250);
    }

    public bool HasBook => Book is not null;

    /// Hoe lang dit deel is, en waar dat vandaan komt.
    ///
    /// Eerst de server: die heeft de duur in de database, en dat is de enige
    /// die tot op de seconde klopt. Maar de server heeft hem niet altijd. Bij
    /// een mp3 zonder Xing-kop geeft `scan.js` 0, en dat is geen zeldzaamheid —
    /// het is hier de reden dat elk deel als `0:00` stond. Een duur van 0 is dus
    /// "onbekend", niet "leeg", en mag nooit als lengte worden gebruikt: een
    /// speler die denkt dat het deel nul seconden lang is, springt overal heen
    /// naar 0 en bewaart overal 0, en dan "werkt" hervatten zonder iets te doen.
    ///
    /// Dus: is de duur 0, dan neemt de lezer het. Zijn eigen lengte is een
    /// schatting bij een mp3 zonder kop, en een schatting is beter dan een klok
    /// die stilstaat.
    public TimeSpan PartLength
    {
        get
        {
            if (Book is not null && Part < Book.Tracks.Count
                && Book.Tracks[Part].Duration > 0)
                return TimeSpan.FromSeconds(Book.Tracks[Part].Duration);

            var reader = _reader;
            if (reader is null) return TimeSpan.Zero;
            try
            {
                var own = reader.TotalTime;
                return own > TimeSpan.Zero ? own : TimeSpan.Zero;
            }
            catch (Exception e) when (e is InvalidOperationException
                                         or NotSupportedException
                                         or ObjectDisposedException
                                         or IOException)
            {
                // Nog geen frame binnen; de klok zegt het over een tik of vier.
                return TimeSpan.Zero;
            }
        }
    }

    public TimeSpan Position
    {
        get
        {
            var reader = _reader;
            if (reader is null) return TimeSpan.Zero;
            try
            {
                // CurrentTime kan buiten de lengte vallen op het allerlaatste
                // frame, en een voortgangsbalk die 100,4% laat zien is een
                // voortgangsbalk die liegt
                var at = reader.CurrentTime;
                var length = PartLength;
                return at < TimeSpan.Zero ? TimeSpan.Zero
                     : length > TimeSpan.Zero && at > length ? length : at;
            }
            catch (Exception e) when (e is InvalidOperationException or NotSupportedException or ObjectDisposedException)
            {
                return TimeSpan.Zero;
            }
        }
    }

    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            ApplyVolume();
            _store.SetSound(_volume, _muted);
            Changed?.Invoke();
        }
    }

    public bool Muted
    {
        get => _muted;
        set
        {
            _muted = value;
            ApplyVolume();
            _store.SetSound(_volume, _muted);
            Changed?.Invoke();
        }
    }

    private void ApplyVolume()
    {
        if (_out is not null) _out.Volume = (float)(_muted ? 0 : _volume);
    }

    /// Hoeveel seconden de slaaptimer nog heeft, of `null` als hij uit staat.
    public TimeSpan? SleepLeft
    {
        get
        {
            var left = _sleepUntil - DateTimeOffset.Now;
            return left > TimeSpan.Zero ? left : null;
        }
    }

    // --- laden --------------------------------------------------------------

    /// Zet de speler klaar op een deel van een boek, vanaf een aantal seconden.
    ///
    /// Er begint niets. `Play` doet dat, en dat is een aparte aanroep omdat een
    /// app die vanzelf begint te praten een app is die je niet meer kunt
    /// negeren.
    ///
    /// Geeft de server nog geen mp3 van dit deel, dan wordt er gewacht tot hij
    /// die heeft. Wachten is hier een toestand met een tekst erbij, geen stilte:
    /// de server is dan aan het omzetten, en dat duurt bij een heel boek even.
    public void Load(BookDetail book, int part, int secondsFromStart, bool autoplay)
    {
        ArgumentNullException.ThrowIfNull(book);
        if (book.Tracks.Count == 0)
        {
            Book = book;
            Part = 0;
            StopOutput();
            Announced?.Invoke(I18n.T("noParts"));
            Changed?.Invoke();
            return;
        }

        part = Math.Clamp(part, 0, book.Tracks.Count - 1);
        var track = book.Tracks[part];

        TearDown();
        Book = book;
        Part = part;

        // De seconden van de aanroep zijn de voortgang, en die komen van de
        // server: `MainWindow.Book` geeft `book.Progress` mee, en dat is wat de
        // server over dit boek weet. Er staat hier geen eigen boekje bij, want
        // twee bronnen die van elkaar verschillen geven een plek die in geen van
        // beide staat — en dat is precies het "hervatten doet niets" waar dit
        // programma om begonnen is.
        _lastSavedSecond = -1;
        Open(track, Math.Max(0, secondsFromStart), autoplay);
    }

    /// Haal de speler leeg. Het boek blijft staan, want het paneel onderin moet
    /// blijven staan tot je het wegklikt — anders verdwijnt het scherm waarin je
    /// op "pauze" hebt gedrukt.
    public void Stop()
    {
        if (Book is null) return;
        SavePlace();
        TearDown();
        IsPlaying = false;
        Waiting = Waiting.None;
        StopSleeper();
        Changed?.Invoke();
    }

    private void TearDown()
    {
        // Een wachtende sprong hoort bij het geluid dat toen open was. Na een
        // ander boek zou hij daar terechtkomen, en dan springt de speler in een
        // boek dat je niet hebt aangeklikt naar een seconde uit een ander boek.
        _pendingSeek = null;
        StopOutput();
        _stream?.Dispose();
        _stream = null;
        _reader?.Dispose();
        _reader = null;
    }

    private void StopOutput()
    {
        var output = _out;
        _out = null;
        if (output is null) return;
        try { output.Stop(); } catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException) { /* al weg */ }
        output.Dispose();
    }

    // --- spelen -------------------------------------------------------------

    public void Play()
    {
        if (Book is null) return;
        if (Book.Tracks.Count == 0)
        {
            Announced?.Invoke(I18n.T("noParts"));
            return;
        }
        if (_reader is not null)
        {
            if (IsPlaying) return;
            TryPlay();
            return;
        }
        // Er is nog geen mp3 binnen. Opnieuw vragen: het kan zijn dat de server
        // inmiddels klaar is, en het kan zijn dat hij nog niet begonnen is.
        _lastSavedSecond = -1;
        Open(Book.Tracks[Part], 0, true);
    }

    public void Pause()
    {
        if (_out is null) return;
        SavePlace();
        _out.Pause();
        IsPlaying = false;
        StopSleeper();
        Changed?.Invoke();
    }

    public void Toggle()
    {
        if (Waiting != Waiting.None) return; // er is nog niets om te pauzeren
        if (IsPlaying) Pause();
        else Play();
    }

    private void TryPlay()
    {
        if (_out is null || _reader is null) return;
        try
        {
            _out.Play();
            IsPlaying = true;
        }
        catch (Exception e) when (e is NAudio.MmException or ObjectDisposedException or InvalidOperationException)
        {
            IsPlaying = false;
            Announced?.Invoke(I18n.T("noSound", ("name", e.Message)));
        }
        Changed?.Invoke();
    }

    /// Spring in dit deel heen en weer. Het aantal seconden wordt begrensd, zodat
    /// "30 seconden terug" aan het begin van een deel gewoon het begin geeft in
    /// plaats van een sprong terug in het vorige deel — wat een speler zonder
    /// boekcontext doet, en hier zou het de verkeerde kant opgaan.
    public void Skip(int seconds)
    {
        if (_reader is null) return;
        var at = Position.TotalSeconds + seconds;
        SeekInPart((int)Math.Round(at));
    }

    public void SeekInPart(int second)
    {
        if (_reader is null) return;
        var length = PartLength;
        // Begrenzen kan alleen als er een lengte is om aan te begrenzen. Met een
        // lengte van 0 (onbekend) begrensde dit alles naar 0, en dan sprong
        // "30 seconden terug" en de voortgangsbalk en het begin van het boek alle
        // drie naar het begin — een speler die op alles wat je doet naar het
        // begin springt is een speler die hervatten niet kan.
        var cap = length > TimeSpan.Zero
            ? Math.Max(0, (int)length.TotalSeconds - 1)
            : int.MaxValue;
        _pendingSeek = TimeSpan.FromSeconds(Math.Clamp(second, 0, cap));
        _seekTries = 0;
        TryPendingSeek();
    }

    // Een plek die pas later gezet kan worden, en waarom.
    //
    // De mp3-lezer gooit `CurrentTime` weg zolang er nog geen frame binnen is,
    // en dat is hier de eerste milliseconde na het openen. De site heeft
    // precies hetzelfde probleem en lost het op dezelfde manier op: niet bij het
    // openen, maar bij `onloadedmetadata`, dus als de metadata binnen is
    // (player.js:62). Dus niet in de aanroep, maar in de klok: daar blijft het
    // staan tot het lukt, en het is de enige plaats waar ook zichtbaar is of
    // er al geluid is.
    private TimeSpan? _pendingSeek;
    private int _seekTries;

    private void TryPendingSeek()
    {
        if (_pendingSeek is not { } where) return;
        var reader = _reader;
        if (reader is null) { _pendingSeek = null; return; }

        try
        {
            reader.CurrentTime = where;
            _pendingSeek = null;
            _seekTries = 0;
            SavePlace(force: true);
            Changed?.Invoke();
        }
        catch (Exception e) when (e is InvalidOperationException
                                     or NotSupportedException
                                     or ObjectDisposedException)
        {
            // Nog geen frame binnen. Dat is een toestand en geen fout, en het
            // duurt een tick of twee. Na twee seconden is het geen toestand
            // meer en moet er iets gezegd worden.
            if (++_seekTries > 8)
            {
                _pendingSeek = null;
                Announced?.Invoke(I18n.T("couldNotPlay", ("name", e.Message)));
            }
        }
        catch (Exception e) when (e is ArgumentOutOfRangeException or IOException)
        {
            // De plek ligt voorbij het einde van dit deel: het bestand is
            // vervangen of korter geworden. Niet niets doen, maar begin opnieuw
            // en zeg het.
            _pendingSeek = null;
            reader.CurrentTime = TimeSpan.Zero;
            SavePlace(force: true);
            Announced?.Invoke(I18n.T("positionBehind", ("name", e.Message)));
            Changed?.Invoke();
        }
    }

    public void NextPart()
    {
        if (Book is null) return;
        if (Part + 1 >= Book.Tracks.Count)
        {
            // Het laatste deel is uit. De plek staat nu aan het eind daarvan, en
            // de server zet daar zijn tik op: dat is de enige plek waar dat
            // gebeurt. Vanaf hier het boek opnieuw beginnen is opnieuw afspelen,
            // en niet "het volgende".
            SavePlace(force: true);
            PartEnded?.Invoke();
            return;
        }
        Load(Book, Part + 1, 0, IsPlaying);
    }

    public void PreviousPart()
    {
        if (Book is null) return;
        if (Part == 0)
        {
            SeekInPart(0);
            return;
        }
        Load(Book, Part - 1, 0, IsPlaying);
    }

    public void Restart()
    {
        if (Book is null) return;
        Load(Book, Part, 0, IsPlaying);
    }

    // --- de mp3 ophalen -----------------------------------------------------

    /// Vraagt de mp3 van dit deel op en zet er een speler op. Is de server er nog
    /// niet mee, dan wordt er gewacht en komt er een voortgangsregel in beeld.
    ///
    /// Dit is de tweede helft van de beslissing "alleen mp3". De eerste helde is
    /// dat er één decoder is; deze is dat er geen tweede is die het overneemt
    /// wanneer het misgaat. Wat hier misgaat wordt gezegd, en niet omzeild.
    private void Open(Track track, int fromSeconds, bool autoplay)
    {
        Waiting = Waiting.Converting;
        ConvertingDone = 0;
        ConvertingTotal = 0;
        Changed?.Invoke();

        _ = FetchAndPlay(track, fromSeconds, autoplay);
    }

    private async Task FetchAndPlay(Track track, int fromSeconds, bool autoplay)
    {
        try
        {
            var answer = await _collection.Mp3(track.Id, _stop.Token);
            while (answer.State == Mp3State.Working)
            {
                ConvertingDone = answer.Done;
                ConvertingTotal = answer.Total;
                Waiting = Waiting.Converting;
                Changed?.Invoke();

                // Twee seconden is genoeg om niet te vragen naar iets wat nog
                // lang niet klaar is, en kort genoeg om niet te wachten alsof er
                // iets mis is. Bij een heel boek van tien uur duurt het omzetten
                // enkele minuten; wie daarbij niets ziet, denkt dat de app
                // vastzit.
                await Task.Delay(TimeSpan.FromSeconds(2), _stop.Token);

                // Intussen kan de gebruiker op een ander boek geklikt hebben. Dan
                // is dit antwoord voor niemand meer, en de stream die hieronder
                // open gaat zou voor het verkeerde boek spelen.
                if (Book is null || Book.Tracks.Count == 0 || Book.Tracks[Part].Id != track.Id) return;

                answer = await _collection.Mp3(track.Id, _stop.Token);
            }

            switch (answer.State)
            {
                case Mp3State.Failed:
                    Waiting = Waiting.None;
                    IsPlaying = false;
                    Changed?.Invoke();
                    // Twee regels, niet één. De eerste zegt wat er misging, de
                    // tweede zegt dat er geen tweede decoder is die het alsnog
                    // zou kunnen. Zonder die tweede leest "het kon niet" als een
                    // storing die vanzelf voorbijgaat, en dat is precies de
                    // verwarring die deze app wil voorkomen.
                    Announced?.Invoke(I18n.T("convertingFailed",
                        ("name", answer.Reason ?? I18n.T("noAnswer"))) + " " + I18n.T("onlyMp3"));
                    return;

                case Mp3State.Gone:
                    Waiting = Waiting.None;
                    IsPlaying = false;
                    Changed?.Invoke();
                    Announced?.Invoke(answer.Reason ?? I18n.T("notFound"));
                    return;

                case Mp3State.Working:
                    return; // de lus hierboven stopt alleen op een andere toestand
            }

            // Vanaf hier is er mp3. De inhoud van het antwoord is nog niet
            // gelezen en wordt hier meteen aan de stroom gegeven, die het met
            // Range aanvraagt. Dat is twee keer vragen om hetzelfde: één om de
            // lengte te weten en één om het geluid te lezen. Dat kost één
            // verzoek en geen enkele byte, en het is de enige manier om te
            // weten hoe lang het hoofdstuk is zonder het te downloaden.
            answer.Content!.Dispose();
            await PlayTheStream(new Uri($"{_collection.BaseUrl}/api/mp3/{track.Id}"),
                               track, fromSeconds, autoplay);
        }
        catch (OperationCanceledException)
        {
            // het scherm sluit, of er is op een ander boek geklikt
        }
        catch (Fault f) when (f.Key is "noMp3Route" or "noMp3RouteNothingSaid")
        {
            // De server kent `/api/mp3` niet. Dat is geen zeldzaamheid en geen
            // storing: `player.js:61` zet `audio.src = /api/stream/${t.id}`, en
            // dat is de route die de webspeler gebruikt. Er is dus een weg die
            // vaststaat te werken, en die wordt hier geprobeerd voordat er iets
            // aan de lezer te zien valt.
            //
            // Wel eerst even zeggen dat de mp3-weg eruit ligt, want dat is waar
            // en de app gaat het nu proberen zonder de lezer te vragen.
            App.Trace("de mp3-route bestaat niet op deze server; val terug op /api/stream, "
                      + "de route die de site gebruikt");
            try
            {
                await PlayTheStream(new Uri($"{_collection.BaseUrl}/api/stream/{track.Id}"),
                                   track, fromSeconds, autoplay);
                return;
            }
            catch (OperationCanceledException) { return; }
            catch (NotMp3 notMp3)
            {
                // De weg werkt, maar het is geen mp3. Dat is een andere klacht
                // dan een route die ontbreekt, en hij vraagt om iets anders:
                // omzetten in de webinterface. Zeggen dat de server te oud is
                // zou hier een leugen zijn — de server is nieuw genoeg.
                Waiting = Waiting.None;
                IsPlaying = false;
                Changed?.Invoke();
                Announced?.Invoke(I18n.T("notMp3", ("kind", notMp3.Kind)));
                return;
            }
            catch (Exception e2)
            {
                // Twee onduidelijkheden achter elkaar, en de tweede weegt
                // zwaarder: er is geen mp3-route, en de weg die de site neemt
                // werkt ook niet. Eerst die laatste, want dat is waar je iets
                // mee kunt, en daarna de eerste, want die staat ook waar.
                Waiting = Waiting.None;
                IsPlaying = false;
                Changed?.Invoke();
                Announced?.Invoke(I18n.T("noStream", ("said", EénRegel.Van(e2.Message))) + " " + f.Text);
                return;
            }
        }
        catch (Exception e)
        {
            Waiting = Waiting.None;
            IsPlaying = false;
            Changed?.Invoke();

            // Een gebroken of niet te lezen bestand is iets anders dan een
            // verbinding die wegviel, en de twee hebben een eigen regel: bij een
            // bestand is het ook nog nuttig te zeggen dat er maar één formaat kan.
            if (e is IOException)
                Announced?.Invoke(I18n.T("noSoundFile") + " " + I18n.T("onlyMp3")
                                  + " (" + e.Message + ")");
            else
                Announced?.Invoke(I18n.T("couldNotPlay", ("name", e.Message)));
        }
    }

    /// Zet een speler op de stroom die deze url geeft, en begint bij `fromSeconds`.
    ///
    /// Eén plek, voor beide routes. Dat is niet netheid om de rede: het was een
    /// kopie van zeven regels, en toen de mp3-route viel moest die kopie apart
    /// worden bijgehouden, en een speler die op twee plaatsen staat afspelen is
    /// een speler waarvan één van de twee plekken verouderd raakt zonder dat
    /// iemand het merkt.
    private async Task PlayTheStream(Uri uri, Track track, int fromSeconds, bool autoplay)
    {
        var stream = await RangeStream.OpenAsync(_collection.Http, uri, _stop.Token);

        // Eerst de vraag: is dit mp3? De server zegt het zelf, en hij weet het
        // beter dan de eerste bytes die hier binnenkomen. Let op de ruimte in
        // "audio/mpeg" — het antwoord mag "audio/mpeg; charset=binary" zijn.
        var kind = stream.ContentType;
        if (kind is not null && kind.Length > 0
            && kind != "audio/mpeg"
            && kind != "application/octet-stream")
        {
            stream.Dispose();
            throw new NotMp3(kind);
        }

        var reader = new Mp3FileReaderBase(stream, factory => new AcmMp3FrameDecompressor(factory));
        var output = new WaveOut();
        output.Init(reader);
        output.Volume = (float)(_muted ? 0 : _volume);
        output.PlaybackStopped += OnStopped;

        _stream = stream;
        _reader = reader;
        _out = output;

        Waiting = Waiting.None;

        // Beginnen waar je was, maar niet in deze aanroep: de mp3-lezer weigert
        // `CurrentTime` zolang er nog geen frame binnen is. `SeekInPart` legt
        // het vast en de klok zet het zodra het kan.
        if (fromSeconds > 0) SeekInPart(fromSeconds);

        if (autoplay) TryPlay();
        else IsPlaying = false;
        Changed?.Invoke();
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        // Dit komt op de draad van het geluid, niet van het scherm.
        if (e.Exception is not null)
        {
            IsPlaying = false;
            Announced?.Invoke(I18n.T("couldNotPlay", ("name", e.Exception.Message)));
            return;
        }
        PartEnded?.Invoke();
    }

    // --- de klok ------------------------------------------------------------

    /// Vier keer per seconde. Doet drie dingen die alle drie stilte in een
    /// zichtbare klacht moeten veranderen: de tijd meebewegen, de voortgangsbalk
    /// bijhouden, en de plek bewaren.
    private void Tick(object? state)
    {
        try
        {
            if (_disposed) return;

            // Een plek die nog gezet moet worden krijgt hier zijn kans, en wel
            // vóór alles: zolang die wacht is er een klok die op 0:00 staat en
            // een speler die begint te spelen waar je niet was.
            TryPendingSeek();

            if (Waiting == Waiting.Converting)
            {
                // Tijdens het omzetten is er geen stroom om mee te gillen, maar
                // wel een vraag te stellen: is de verbinding er nog? Zo niet, dan
                // moet dat gezegd worden in plaats van eeuwig te wachten op een
                // antwoord dat niet meer komt.
                Changed?.Invoke();
                return;
            }

            if (IsPlaying) SavePlace();

            var sleep = SleepLeft;
            if (sleep is null) StopSleeper();
            else if (sleep < TimeSpan.FromSeconds(3) && _sleeping)
            {
                // Bijna af: het geluid gaat eraan, en niet midden in een
                // hoofdstuk zonder dat er iets is gezegd.
                _sleeping = false;
                if (_out is not null) _out.Volume = 0f;
                Announced?.Invoke(I18n.T("sleepTimerDone"));
            }

            Changed?.Invoke();
        }
        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException
                                     or TaskCanceledException or OperationCanceledException)
        {
            // het scherm slikt terwijl de klok loopt: niets meer te doen. Ook
            // de afgebroken klok van het sluiten hoort hier, want die zegt
            // niets over het boek en zou elke keer een regel in `fouten.log`
            // opleveren die er niet staat.
        }
    }

    /// De plek bewaren, maar niet elke seconde.
    ///
    /// Eén keer per vijf seconden is genoeg: wie de app slikt verliest dan
    /// hoogstens vijf seconden van een boek van uren, en de server krijgt geen
    /// vijfhonderd verzoeken per uur voor een getal dat niemand zo precies
    /// nodig heeft.
    private void SavePlace(bool force = false)
    {
        if (Book is null || _reader is null) return;
        var second = (int)Position.TotalSeconds;
        if (!force && second == _lastSavedSecond) return;
        if (!force && Math.Abs(second - _lastSavedSecond) < 5) return;
        _lastSavedSecond = second;

        if (string.IsNullOrEmpty(_store.User)) return;

        var user = _store.User;
        var bookId = Book.Id;
        var track = Part;
        _ = _collection.SavePlace(bookId, track, second, _stop.Token)
            .ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    // Het bewaard blijft zoals het is: de app hoeft niet te
                    // schreeuwen omdat de server even niet kon antwoorden. Maar
                    // stil zijn mag het ook niet, en daarom staat het in de
                    // vensterbalk zolang het aan de gang is.
                    t.Exception?.Handle(e => { _ = e; return true; });
                    SaveFailed?.Invoke();
                }
            }, TaskScheduler.Default);
    }

    /// Het bewaren naar de server is mislukt. De voortgang staat dan nergens, want
    /// die wordt nergens anders bewaard, en het scherm zegt dat. Anders lijkt het
    /// alsof je plek bewaard is, en dat is precies wat er niet is.
    public event Action? SaveFailed;

    public void StartSleepTimer(int minutes)
    {
        StopSleeper();
        if (minutes <= 0) return;
        _sleepUntil = DateTimeOffset.Now.AddMinutes(minutes);
        _sleeping = true;
        Changed?.Invoke();
    }

    public void CancelSleepTimer()
    {
        StopSleeper();
        ApplyVolume();
        Changed?.Invoke();
    }

    private void StopSleeper()
    {
        _sleeping = false;
        _sleepUntil = default;
    }

    /// Houd de knop van de speler gelijk aan wat er werkelijk gebeurt. Wordt
    /// aangeroepen als er iets kapotgaat dat de speler zelf niet kan zien.
    public void Complain(Exception why) => Announced?.Invoke(I18n.T("couldNotPlay", ("name", why.Message)));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        _clock?.Dispose();
        StopSleeper();
        SavePlace(force: true);
        if (_out is not null) _out.PlaybackStopped -= OnStopped;
        TearDown();
        _stop.Dispose();
    }
}

/// De server stuurde geen mp3, en dat is een eigen soort klacht.
///
///
/// Er is één decoder (zie `ONDERZOEK-AUDIO.md`) en die spreekt geen m4a, ogg of
/// flac. Dat is een bewuste keuze, want een tweede decoder die half werkt geeft
/// een speler die soms geluid geeft en soms niet. Maar een bewuste keuze die je
/// niet hoort zitten is geen keuze, en dus zegt dit wat er binnenkwam, zodat
/// de klacht kan zeggen dat omzetten in de webinterface de oplossing is — en
/// niet "het werkt niet", wat een klacht is waar niemand iets mee kan.
public sealed class NotMp3(string kind) : Exception($"the server sent {kind}, not audio/mpeg")
{
    public string Kind { get; } = kind;
}

/// Een exceptiontekst die in een toast van twee regels past.
///
/// Het gaat om de tekst van de server, en die is zelden één regel: een
/// HTML-pagina met een `<pre>` erbij is er zes. Zes regels in een toast die twee
/// regels hoog is, betekent dat de regel die iets zegt eronder valt. Dus: de
/// merktekens eruit, alles op één regel, en afgekapt op een lengte die past.
internal static class EénRegel
{
    internal static string Van(string text) => Collection.OneLineHeader(text);
}
