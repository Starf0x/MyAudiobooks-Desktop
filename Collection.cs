using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace MyAudiobooks;

/// Iets wat de server niet goed deed, in het Nederlands of Engels, en klaar om op
/// het scherm te zetten.
///
/// Elk bericht zegt wat er misging, want een app die stil blijft staan is erger
/// dan een die zegt dat het niet kan. `I18n.T` geeft de sleutel terug als de
/// vertaling ontbreekt, dus een onbekende reden is zichtbaar in plaats van leeg.
public sealed class Fault(string key, params (string Name, object? Value)[] vars) : Exception(I18n.T(key, vars))
{
    public string Key { get; } = key;

    /// Dezelfde fout, maar voor wanneer de taal verandert terwijl de app draait.
    public string Text => I18n.T(Key, Vars);
    private (string Name, object? Value)[] Vars { get; } = vars;
}

public enum Mp3State
{
    /// Er is mp3. `Content` is het antwoord, nog niet gelezen.
    Ready,

    /// De server is hem aan het maken. `Done` en `Total` zijn seconden, en
    /// `Total` is nog niet bekend zolang de eerste helft niet binnen is.
    Working,

    /// Het lukte niet. `Reason` is wat de server erover zei.
    Failed,

    /// Dit spoor of het bestand er niet meer. Dit is iets anders dan een fout:
    /// het is waar, en opnieuw proberen verandert er niets aan.
    Gone,
}

public sealed record Mp3Answer(Mp3State State, HttpContent? Content, double Done, double Total, string? Reason)
{
    public static Mp3Answer Ready(HttpContent content) => new(Mp3State.Ready, content, 0, 0, null);
    public static Mp3Answer Working(double done, double total) => new(Mp3State.Working, null, done, total, null);
    public static Mp3Answer Failed(string reason) => new(Mp3State.Failed, null, 0, 0, reason);
    public static Mp3Answer Gone(string reason) => new(Mp3State.Gone, null, 0, 0, reason);
}

/// De verbinding met de collectie-server.
///
/// Eén `HttpClient` voor de hele levensduur van de app. Iedere keer een nieuwe
/// aanmaken is de klassieke manier om het bestandshandvels van Windows te laten
/// lekken: elke instantie houdt eigen verbindingen vast, en die komen pas vrij
/// als de app stopt. In een app die een boek lang aan één stroom hangt is dat het
/// verschil tussen een programma dat maanden open kan staan en een dat vastloopt.
///
/// De sessiecookie hoort hier, in een `CookieContainer`. In de Electron-versie
/// bewaarde Chromium die zelf; hier is er geen Chromium, dus de app doet het zelf
/// en bewaart de cookie versleuteld (zie `Store`).
public sealed class Collection : IDisposable
{
    private readonly HttpClient _http;
    private readonly CookieContainer _cookies = new();
    private bool _disposed;

    public string BaseUrl { get; private set; }
    public string User { get; set; } = "";

    /// De verbinding zelf, voor de stroom die de speler leest.
    ///
    /// Die stroom is geen tweede `HttpClient` en mag het ook niet zijn: hij zou
    /// de sessiecookie niet meesturen en elke aanroep een eigen verbinding
    /// openen. Dus dezelfde, en dat is precies de reden dat dit hier openbaar
    /// staat en niet elders.
    public HttpClient Http => _http;

    /// Hoe lang een kort verzoek mag hangen voordat de app het opgeeft. Het mp3
    /// verzoek krijgt geen klok: dat is een stroom die zo lang duurt als een
    /// hoofdstuk duurt, en daar is geen redelijke limiet voor.
    private static readonly TimeSpan Short = TimeSpan.FromSeconds(20);

    public Collection(string baseUrl)
    {
        BaseUrl = baseUrl.TrimEnd('/');
        _http = new HttpClient(new HttpClientHandler
        {
            CookieContainer = _cookies,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            // het is één server op één netwerk, en hij is vaak een container op
            // thuisnetwerk hardware. Veel verbindingen ervoor opzetten helpt daar
            // niets en houdt alleen sockets vast.
            MaxConnectionsPerServer = 4,
        })
        {
            Timeout = Timeout.InfiniteTimeSpan, // elke aanroep geeft zelf zijn eigen klok
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MyAudiobooks-Wpf/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    public void SetBaseUrl(string baseUrl)
    {
        BaseUrl = baseUrl.TrimEnd('/');
        _cookies.GetCookies(new Uri(BaseUrl)).ToList().ForEach(c => c.Expired = true);
    }

    /// De cookie van de vorige sessie erin zetten, als er een was.
    public void AdoptCookie(string? cookie)
    {
        if (string.IsNullOrWhiteSpace(cookie)) return;
        try
        {
            var host = new Uri(BaseUrl).Host;
            _cookies.Add(new Cookie("mabc_session", cookie, "/", host)
            {
                HttpOnly = true,
                // niet opgeslagen in het bestand van de gebruiker: dat is de taak
                // van `Store`, met zijn eigen versleuteling. Hier is het alleen een
                // waarde in het geheugen van dit proces.
                Expires = DateTimeOffset.MaxValue.Date,
            });
        }
        catch (CookieException)
        {
            // Een cookie die dit programma niet kan gebruiken is hetzelfde als
            // geen cookie: de app meldt zich gewoon opnieuw aan.
        }
    }

    /// De cookie zoals de server hem nu heeft, om te bewaren voor de volgende start.
    public string? CurrentCookie()
    {
        try
        {
            var bag = _cookies.GetCookies(new Uri(BaseUrl));
            foreach (Cookie c in bag)
                if (c.Name is "mabc_session" or "connect.sid")
                    return c.Value;
        }
        catch (Exception e) when (e is CookieException or UriFormatException)
        {
            // het adres is onbruikbaar; dat hoort de aanroeper al te weten
        }
        return null;
    }

    public void ClearCookies()
    {
        foreach (Cookie c in _cookies.GetCookies(new Uri(BaseUrl)))
            c.Expired = true;
    }

    // --- de korte verzoeken -------------------------------------------------

    public async Task<T> Get<T>(string path, CancellationToken stop) where T : class
    {
        using var reply = await Send(HttpMethod.Get, path, null, Short, stop);
        return Read<T>(await reply.Content.ReadAsStringAsync(stop), reply.StatusCode, path);
    }

    /// Een lijst. De server stuurt soms een lege lijst, soms `null`; beide zijn
    /// hier hetzelfde: niets, dus niets om te tonen.
    public async Task<List<T>> List<T>(string path, CancellationToken stop) where T : class
    {
        using var reply = await Send(HttpMethod.Get, path, null, Short, stop);
        var body = await reply.Content.ReadAsStringAsync(stop);
        if (string.IsNullOrWhiteSpace(body)) return new List<T>();
        try
        {
            return JsonSerializer.Deserialize<List<T>>(body, Wire.Options) ?? new List<T>();
        }
        catch (JsonException e)
        {
            // Zelfde als in `Read<T>`: het hele antwoord in het log, want hier
            // is het net zo goed onduidelijk wat er misging.
            App.Trace($"     kan een lijst van {typeof(T).Name} niet lezen op {path}: {e.Message}");
            App.Trace($"     het antwoord was: {Tail(body)}");
            throw Why(reply.StatusCode, body, path);
        }
    }

    public Task<Home> Home(CancellationToken stop) => Get<Home>(Who("/api/home"), stop);
    public Task<Stats> Stats(CancellationToken stop) => Get<Stats>(Who("/api/stats"), stop);
    public Task<List<Genre>> Genres(CancellationToken stop) => List<Genre>("/api/genres", stop);
    public Task<List<Book>> Listened(CancellationToken stop) => List<Book>(Who("/api/listened"), stop);

    public Task<List<Author>> Authors(string genre, CancellationToken stop) =>
        List<Author>($"/api/authors?genre={Uri.EscapeDataString(genre)}", stop);

    public Task<BooksPage> BooksByAuthor(string genre, string author, CancellationToken stop) =>
        Get<BooksPage>(Who($"/api/books?genre={Uri.EscapeDataString(genre)}&author={Uri.EscapeDataString(author)}"), stop);

    public Task<BooksPage> BooksBySeries(string genre, string series, CancellationToken stop) =>
        Get<BooksPage>(Who($"/api/books?genre={Uri.EscapeDataString(genre)}&series={Uri.EscapeDataString(series)}"), stop);

    public Task<List<Book>> Search(string q, CancellationToken stop) =>
        List<Book>(Who($"/api/search?q={Uri.EscapeDataString(q)}"), stop);

    public Task<BookDetail> Book(long id, CancellationToken stop) =>
        Get<BookDetail>(Who($"/api/books/{id}"), stop);

    public async Task<T> Post<T>(string path, object body, CancellationToken stop) where T : class
    {
        using var reply = await Send(HttpMethod.Post, path, body, Short, stop);
        var text = await reply.Content.ReadAsStringAsync(stop);
        if (string.IsNullOrWhiteSpace(text)) return Read<T>("{}", reply.StatusCode, path);
        return Read<T>(text, reply.StatusCode, path);
    }

    public Task<ProgressReply> SavePlace(long bookId, int trackIdx, int position, CancellationToken stop) =>
        Post<ProgressReply>("/api/progress",
            new { user = User, bookId, trackIdx, position }, stop);

    public Task<ProgressReply> MarkListened(long bookId, bool done, CancellationToken stop) =>
        Post<ProgressReply>("/api/listened", new { user = User, bookId, done }, stop);

    public async Task SignIn(string name, string password, CancellationToken stop)
    {
        using var reply = await Send(HttpMethod.Post, "/api/account/signin",
            new { name, password }, Short, stop);
        var text = await reply.Content.ReadAsStringAsync(stop);
        if (reply.IsSuccessStatusCode) return;
        // een 401 heet hier "verkeerd wachtwoord", en dat is de enige reden die de
        // app hier kan geven zonder iets te weten over wat er aan de andere kant
        // gebeurt. Alles anders: de fout van de server zelf, want die kan iets
        // zeggen wat wij niet kunnen verzinnen.
        throw reply.StatusCode == System.Net.HttpStatusCode.Unauthorized
            ? new Fault("signInFailed")
            : Why(reply.StatusCode, text);
    }

    public async Task<Who> Me(CancellationToken stop)
    {
        using var reply = await Send(HttpMethod.Get, "/api/account/me", null, Short, stop);
        var text = await reply.Content.ReadAsStringAsync(stop);
        if (reply.StatusCode == System.Net.HttpStatusCode.Unauthorized) return new Who();
        return Read<Who>(text, reply.StatusCode, "/api/account/me");
    }

    public async Task SignOut(CancellationToken stop)
    {
        try
        {
            using var _ = await Send(HttpMethod.Post, "/api/account/signout", new { }, Short, stop);
        }
        catch (Fault)
        {
            // Afmelden dat niet lukt is geen reden om hier te blijven hangen: de
            // cookie gaat toch weg, en de volgende start vraagt het opnieuw.
        }
    }

    /// De boeken met een hart. `null` betekent: deze server kent het niet.
    ///
    /// Dat is iets anders dan een lege lijst, en het verschil moet zichtbaar
    /// zijn. Een server zonder deze route antwoordt met de standaard-404 van
    /// express, en dat is onderscheidbaar van een antwoord van een server die
    /// er wél is: de eerste is HTML, de tweede JSON. Bij `null` laat de app het
    /// hartje helemaal weg, want een sectie "Favorieten" die nooit iets kan
    /// bevatten, is iets dat niemand wil zien.
    public async Task<List<Book>?> Favourites(CancellationToken stop)
    {
        var path = Who("/api/favourites");
        using var reply = await Send(HttpMethod.Get, path, null, Short, stop);
        var body = await reply.Content.ReadAsStringAsync(stop);
        if (reply.StatusCode == HttpStatusCode.NotFound) return null;
        if (!reply.IsSuccessStatusCode) throw Why(reply.StatusCode, body, path);
        try
        {
            return JsonSerializer.Deserialize<List<Book>>(body, Wire.Options) ?? new List<Book>();
        }
        catch (JsonException)
        {
            throw Why(reply.StatusCode, body, path);
        }
    }

    public async Task MarkFavourite(long bookId, bool on, CancellationToken stop)
    {
        var path = $"/api/favourites/{bookId}";
        using var reply = await Send(HttpMethod.Post, path, new { on }, Short, stop);
        if (reply.IsSuccessStatusCode) return;
        var body = await reply.Content.ReadAsStringAsync(stop);
        if (reply.StatusCode == HttpStatusCode.NotFound) throw new Fault("noFavouritesHere");
        throw Why(reply.StatusCode, body, path);
    }

    /// De `user` aan het pad hangen, met het teken dat bij dit pad hoort.
    ///
    /// Een pad zonder vraagteken krijgt `?user=…`, en een pad dat er al één heeft
    /// krijgt `&user=…`. Dat klinkt als een kleinigheid en dat is het ook, tot
    /// je hem vergeet: met twee vraagtekens stond er
    /// `author=Anderenaam?user=frank`, en dus een auteur die niemand heet, en dus
    /// een lege lijst zonder enige klacht. Alle routes met een vraagteken ervan
    /// — auteur, serie, zoeken — gaven zo niets. Dat is de soort fout die je
    /// niet ziet, want een lege lijst is een antwoord.
    ///
    /// Het pad gaat er dus héél in: het is het pad ná de vraagteken dat telt,
    /// niet het eerste stuk ervan.
    private string Who(string path) =>
        string.IsNullOrEmpty(User)
            ? path
            : path.Contains('?')
                ? $"{path}&user={Uri.EscapeDataString(User)}"
                : $"{path}?user={Uri.EscapeDataString(User)}";

    // --- de mp3 -------------------------------------------------------------

    /// Vraag de server om mp3 van dit spoor.
    ///
    /// Dit is geen gewone aanroep om één ding terug te krijgen, en de drie manieren
    /// waarop het mis kan gaan zijn alle drie iets anders voor wie er naar kijkt:
    ///
    ///  - **409 met voortgang.** De server is het spoor aan het omzetten vanuit
    ///    m4a of ogg. De app toont dat en vraagt later opnieuw. Niets zeggen zou
    ///    een speler zijn die stil staat.
    ///  - **500 met een reden.** Het lukte niet: geen ffmpeg, of een kapot
    ///    bestand. Opnieuw proberen helpt niet, en de reden van de server is het
    ///    enige wat hier zinvol te zeggen is.
    ///  - **404.** Twee betekenissen, en ze moeten uit elkaar: weg (het spoor of
    ///    het bestand bestaat niet meer) of onbekend (deze server heeft de route
    ///    niet). De route stuurt JSON met een `state`; express zijn standaard-404
    ///    is een HTML-pagina. Dus: is het antwoord JSON, dan kent de server de
    ///    route en is het spoor weg; is het HTML, dan kent de server hem niet en
    ///    moet de app dat zeggen in plaats van te wachten tot er een time-out valt.
    public async Task<Mp3Answer> Mp3(long trackId, CancellationToken stop)
    {
        var url = $"/api/mp3/{trackId}";
        HttpResponseMessage reply;
        try
        {
            // geen klok: het is een stroom die zo lang duurt als het hoofdstuk
            reply = await _http.GetAsync(BaseUrl + url, HttpCompletionOption.ResponseHeadersRead, stop);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException e)
        {
            throw new Fault("unreachable", ("name", e.InnerException?.Message ?? e.Message));
        }

        if (reply.IsSuccessStatusCode)
        {
            // Dit is het enige verzoek van de app dat níet via `Send` gaat, en dat
            // is precies waarom er vanavond niets in het log te zien was terwijl
            // er niets gebeurde. Een speler die stilletjes niets doet is een
            // speler waarvan niemand weet waarom; de hele reden dat dit bestand
            // bestaat is dat elke mislukking iets zegt, en een verzoek dat niet
            // gelogd wordt maakt van elke mislukking iets wat niets zegt.
            App.Trace($"200 OK               GET   {url}  {reply.Content.Headers.ContentType}"
                      + "  (het geluid zelf, niet gelezen)");
            return Mp3Answer.Ready(reply.Content);
        }

        var status = reply.StatusCode;
        var body = "";
        try { body = await reply.Content.ReadAsStringAsync(stop); }
        catch (Exception e) when (e is HttpRequestException or IOException or ObjectDisposedException)
        {
            // het antwoord is half weg; de status alleen is hier genoeg
        }

        // De eerste tekens van het lichaam, want het verschil tussen "de route
        // bestaat niet" en "het spoor is weg" is precies wat hier staat: JSON
        // van de server, of een HTML-pagina van express. Zonder die tekens in het
        // log is het verschil alleen uit het gedrag van de app af te leiden, en
        // dan is er iets te raden in plaats van iets te lezen.
        App.Trace($"{(int)status} {reply.StatusCode,-16} GET   {url}  "
                  + $"{reply.Content.Headers.ContentType}  begin: "
                  + (body.Length > 160 ? body[..160].ReplaceLineEndings(" ") : body.ReplaceLineEndings(" ")));

        switch ((int)status)
        {
            case 409:
            {
                var said = TryRead(body);
                return Mp3Answer.Working(
                    said?.TryGetProperty("done", out var d) == true ? d.GetDouble() : 0,
                    said?.TryGetProperty("total", out var t) == true ? t.GetDouble() : 0);
            }
            case 500:
            {
                var said = TryRead(body);
                var why = said?.TryGetProperty("error", out var e) == true
                    ? e.GetString() : null;
                return Mp3Answer.Failed(why ?? I18n.T("convertingFailed", ("name", "")));
            }
            case 404:
            {
                if (TryRead(body) is { } said)
                {
                    var why = said.TryGetProperty("error", out var e) == true ? e.GetString() : null;
                    return Mp3Answer.Gone(why ?? I18n.T("notFound"));
                }
                reply.Dispose();
                var plain = OneLine(body);
                if (plain.Length == 0)
                    throw new Fault("noMp3RouteNothingSaid");
                if (plain.Length > 140) plain = plain[..140] + "…";
                // Wat de server ook antwoordde, het staat in de melding. Zeggen
                // dat zijn server te oud is zonder het te weten is een gok, en een
                // gok die de lezer op het spoor van een oplossing zet die niet
                // bestaat. Wat hier zeker is: hij gaf geen mp3 en geen JSON, dus
                // hij deed niet wat deze route hoort te doen. Zeg dat, en geef
                // zijn eigen antwoord mee zodat de lezer het kan nakijken.
                //
                // Eén regel, en zonder de merktekens. De volledige tekst staat in
                // `verzoeken.log`; een melding is geen plek voor zes regels HTML,
                // en de site zet er ook één zin in. Er staat hier geen eigen
                // "the server said:" bij, want de regel in `I18n` zegt al wat er
                // volgt — twee keer hetzelfde klinkt alsof het twee dingen zijn.
                throw new Fault("noMp3Route", ("said", plain));
            }
            default:
                reply.Dispose();
                throw Why(status, body);
        }
    }

    private static JsonElement? TryRead(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// De waarde van één HTTP-header, leesbaar in één regel en zonder de
    /// merktekens. Gebruikt door de speler voor de `Content-Type`, want ook die
    /// kan van alles zijn en moet niet over zes regels in een toast staan.
    internal static string OneLineHeader(string value) =>
        value.Length > 200 ? OneLine(value)[..200] + "…" : OneLine(value);

    /// Een antwoord van de server leesbaar in één regel, met zijn merktekens weg.
    ///
    /// Een 404 zonder JSON is meestal een HTML-pagina, en de standaardpagina van
    /// express is zes regels opmaak met erin de ene regel die iets zegt:
    /// `Cannot GET /api/mp3/11`. Die ene regel is wat de lezer nodig heeft, en de
    /// rest is ruis. Dus: de merktekens eruit, de bekende namen terug, en alles
    /// op één regel.
    ///
    /// Alleen de inhoud van `<head>`, `<style>` en `<script>` gaat weg, en niet
    /// de tekst tussen `<` en `>` in het algemeen. Dat verschil is de hele
    /// functie: in een HTML-pagina staat het bericht van de server ín een tag
    /// (`<pre>` bij express, `<title>` bij nginx), en een functie die álle tekst
    /// tussen merktekens weggooit nam dus precies het bericht mee. Het gevolg was
    /// een lege regel, en een lege regel is `noMp3RouteNothingSaid`: de app zei
    /// "de server zei niets" over een server die `Cannot GET /api/mp3/49424`
    /// schreef. Rekenen op tags in plaats van op merktekens is de fix; een
    /// kale tekst zonder tags (de 500 van een proxy) blijft daarmee gewoon
    /// staan, en dat is ook wat je dan wilt lezen.
    ///
    /// En daarna twee keer een regel toegevoegd om een lege regel te vermijden
    /// — eerst één die de sluittags niet herkende, toen één met een toelichting
    /// die zei dat ze er wél waren — terwijl de oorzaak dezelfde bleef: een
    /// beslissing over wat de server zei nemen aan de hand van tekens die er
    /// niet in de tekst zaten. De les is dus niet "nog een voorwaarde", maar dat
    /// een functie die de reden moet bewaren niet mag worden geschreven alsof ze
    /// de vorm opruimt. Deze was geschreven om de vorm op te ruimen.
    private static string OneLine(string body)
    {
        var stil = false;        // tussen <head>, <style> of <script>
        var commentaar = false;  // tussen <!-- en -->
        var zin = new System.Text.StringBuilder(body.Length);
        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];
            if (commentaar)
            {
                if (c == '>' && body[i - 1] == '-' && body[i - 2] == '-') commentaar = false;
                continue;
            }
            if (c == '<')
            {
                if (body.AsSpan(i).StartsWith("<!--"))
                {
                    commentaar = true;
                    zin.Append(' ');
                    i += 3;
                    continue;
                }
                var naam = NaamVan(body, i, out var lengte);
                if (StilVol(naam)) stil = naam[0] != '/';
                i += lengte;
                // Een spatie op elke grens, zodat "GET" en "mp3" niet aan elkaar
                // plakken tot "GETmp3".
                zin.Append(' ');
                continue;
            }
            if (stil) continue;
            if (c is '&')
            {
                var rest = body.AsSpan(i);
                foreach (var (naam, wat) in new (string, string)[]
                         { ("&amp;", "&"), ("&lt;", "<"), ("&gt;", ">"), ("&quot;", "\""), ("&#39;", "'"), ("&nbsp;", " ") })
                {
                    if (rest.StartsWith(naam.AsSpan(), StringComparison.Ordinal))
                    {
                        zin.Append(wat);
                        i += naam.Length - 1;
                        goto next;
                    }
                }
            }
            zin.Append(c);
        next: ;
        }
        return string.Join(' ', zin.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static readonly string[] Stil = ["head", "style", "script"];

    /// Of de tekst tussen deze tag en zijn sluittag niets zegt voor de lezer.
    /// De sluittag telt mee, dus `/head` is hetzelfde antwoord als `head` — en dat
    /// is geen bijdetail: de eerste versie keek alleen naar het openingswoord,
    /// waardoor `</head>` het zwijgen niet opheffde en `<body>` er nog steeds in
    /// zat. Het gevolg was dezelfde lege regel als hierboven, één regel lager.
    private static bool StilVol(string naam) =>
        Stil.Contains(naam.TrimStart('/'), StringComparer.Ordinal);

    /// De naam van de tag die op `i` begint, en hoeveel tekens de hele tag
    /// beslaat.
    ///
    /// De naam begint met `/` als het een sluittag is, en dat is niet cosmetiek:
    /// `OneLine` vraagt zich af of de tekst tussen deze tag en zijn sluittag
    /// niets zegt, en dat kan alleen als het weet dat het om een sluittag gaat.
    /// Een versie die de `/` wegliet — en de toelichting erboven zei wél dat hij
    /// er stond — maakte `</head>` niet herkenbaar als het einde van `<head>`,
    /// waardoor de stilte tot het einde van het document duurde en de 404 van
    /// express alsnog een lege regel opleverde. Derde keer dezelfde les, andere
    /// regel: zie de kop van `OneLine`.
    ///
    /// Zonder afsluitende `>` is de lengte 0, en dan loopt de buitenste lus toch
    /// door: elke stap is minstens één teken.
    private static string NaamVan(string body, int i, out int lengte)
    {
        var j = i + 1;
        var begin = j; // vóór het teken `/`, want dat zit in de naam
        if (j < body.Length && body[j] == '/') j++;
        while (j < body.Length && (char.IsLetterOrDigit(body[j]) || body[j] == '-')) j++;
        var uit = body.IndexOf('>', j);
        lengte = uit > i ? uit - i : 0;
        return body[begin..j].ToLowerInvariant();
    }

    // --- onderop ------------------------------------------------------------

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body, TimeSpan limit, CancellationToken stop)
    {
        using var clock = CancellationTokenSource.CreateLinkedTokenSource(stop);
        clock.CancelAfter(limit);
        try
        {
            var message = new HttpRequestMessage(method, BaseUrl + path);
            if (body is not null)
                message.Content = JsonContent.Create(body);
            message.Headers.Accept.ParseAdd("application/json");
            var reply = await _http.SendAsync(message, HttpCompletionOption.ResponseContentRead, clock.Token);
            App.Trace($"{(int)reply.StatusCode} {reply.StatusCode,-16} {method.Method,-5} {path}"
                      + $"  {reply.Content.Headers.ContentType?.ToString() ?? "(geen soort)"}"
                      + $"  {reply.Content.Headers.ContentLength?.ToString() ?? "?"} bytes");
            return reply;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // de eigen klok van dit verzoek, dus niet het scherm dat sluit
            throw new Fault("unreachable", ("name", I18n.T("noAnswer")));
        }
        catch (HttpRequestException e)
        {
            throw new Fault("unreachable", ("name", e.InnerException?.Message ?? e.Message));
        }
        catch (IOException e)
        {
            // een verbinding die halverwege wegvalt
            throw new Fault("unreachable", ("name", e.Message));
        }
    }

    private static T Read<T>(string body, HttpStatusCode status, string path = "") where T : class
    {
        try
        {
            return Wire.Read<T>(body) ?? Wire.Read<T>("{}")!;
        }
        catch (JsonException e)
        {
            // Het hele antwoord in het log, niet alleen het begin: het verschil
            // tussen "een veld dat C# niet kan lezen" en "iets anders" is vaak
            // één veld verderop te zien dan het begin. Zonder dit moet er elke
            // keer opnieuw een speler voor worden gebouwd om te weten wat er op
            // die server werkelijk staat.
            App.Trace($"     kan {typeof(T).Name} niet lezen op {path}: {e.Message}");
            App.Trace($"     het antwoord was: {Tail(body)}");
            throw Why(status, body, path);
        }
    }

    /// Een status die geen succes is, vertaald naar iets dat iemand kan lezen.
    ///
    /// Waarom zo precies: `notForYou` betekent dat er een ander wachtwoord bij
    /// hoort dan wat is ingetypt, en `unreachable` dat de server niet antwoordt.
    /// Die twee door elkaar gebruiken is een app die zegt dat de server
    /// onbereikbaar is omdat je wachtwoord fout is — en dan typ je het drie keer
    /// opnieuw in.
    ///
    /// En een **2xx die toch misgaat** is een geval van een eigen soort, want
    /// het is geen fout van de server: het antwoord is binnen, alleen niet in
    /// de vorm die de app kan lezen. Dat zeggen met het statusnummer is
    /// nonsens — "de server zei: 200" is een klacht waar niemand iets aan heeft
    /// en die vooral misleidend is. Dus: waar het misging, welke status, en de
    /// eerste letters van wat er inderdaad binnenkwam. Dat laatste staat ook in
    /// `verzoeken.log`, zodat het na te lezen is zonder de app opnieuw te
    /// starten.
    private static Fault Why(HttpStatusCode status, string body, string path = "")
    {
        // `TryGetProperty` op de wortel van een lijst gooit een
        // InvalidOperationException, en die verving ooit de echte klacht met
        // "requires an element of type 'Object'" — een fout over een getal dat
        // nergens in de buurt komt. Dus eerst kijken wat het antwoord is.
        var said = TryRead(body);
        string? fromServer = null;
        if (said is { ValueKind: JsonValueKind.Object } obj && obj.TryGetProperty("error", out var e))
            fromServer = e.GetString();
        if (fromServer is not null) return new Fault("serverSaid", ("name", fromServer));

        if ((int)status is >= 200 and < 300)
        {
            App.Trace($"     onleesbaar antwoord op {path}: {Peek(body)}");
            return new Fault("unreadable",
                ("where", path.Length > 0 ? path : "de server"),
                ("code", ((int)status).ToString()),
                ("body", Peek(body)));
        }

        return (int)status switch
        {
            401 or 403 => new Fault("notForYou"),
            404 => new Fault("notFound", ("name", "")),
            413 => new Fault("tooMuch"),
            429 => new Fault("tooMuch", ("name", "")),
            _ => new Fault("serverSaid", ("name", ((int)status).ToString())),
        };
    }

    /// De eerste letters van een antwoord, op één regel.
    ///
    /// Het hele antwoord in een klacht zetten is geen hulp: het scherm is
    /// ongeschikt voor een pagina HTML. Een regel van honderdvijftig tekens is
    /// genoeg om te zien wat het is — een HTML-pagina, een foutmelding in tekst,
    /// of een lijst waar een voorwerp hoort.
    private static string Peek(string body)
    {
        var flat = string.Join(' ', body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (flat.Length == 0) return "(leeg)";
        return flat.Length <= 180 ? flat : flat[..180] + "…";
    }

    /// Het antwoord zoals het is, maar met het einde in plaats van het begin
    /// zodra het te lang is.
    ///
    /// De eerste vierduizend tekens, want daar staat meestal het gezochte veld
    /// niet. Een lijst van honderd boeken is al twee keer zo lang, en het
    /// verschil zit bijna altijd in het eerste boek of het laatste.
    private static string Tail(string body)
    {
        var flat = string.Join(' ', body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (flat.Length <= 4000) return flat;
        return "…" + flat[^4000..];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }
}
