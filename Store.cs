// `System.IO` staat niet in de impliciete usings van een WPF-project, dus die
// zet ik hier neer in plaats van overal te herhalen dat er bestanden zijn.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyAudiobooks;

/// Wat de app onthoudt tussen twee starts: het adres van de server, je naam,
/// het volume, de opengeklapte genres en de grootte van het venster.
///
/// Eén bestand, geschreven zodra er iets verandert. Het wachtwoord staat er niet
/// in en komt er ook niet in: het is alleen nodig om aan te melden, en de
/// sessiecookie staat versleuteld in een eigen bestand (zie `Cookie`).
///
/// Waar je in een boek was staat hier niet in, en dat is een bewuste keuze.
/// Die voortgang hoort bij de collectie-server: hij schrijft hem, en hij geeft
/// hem terug. Een tweede kop ernaast is een tweede waarheid die daar vandaan
/// loopt, en dan is er geen manier meer om te zeggen welke van de twee klopt.
/// Eén boek, één plek, één bron.
///
/// Er is geen `playing`. In de Electron-versie stond dat er wel, om de volgende
/// start vanzelf te laten beginnen, en dat is precies wat niet de bedoeling is: een
/// app die je openzet en die dan zelf het geluid aanzet is een app die begint te
/// praten terwijl je nog niets hebt gedaan. De stilte wordt niet opgeheven zonder
/// dat je ergens op hebt gedrukt.
public sealed class Store
{
    public const string DefaultBaseUrl = "https://mabc.the-airheads.com";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private string _file = "";
    private string _spare = "";
    private string _cookieFile = "";
    private string? _warning;

    public string BaseUrl { get; private set; } = DefaultBaseUrl;
    public string User { get; private set; } = "";
    public double Volume { get; private set; } = 1;
    public bool Muted { get; private set; }
    public List<string> OpenGenres { get; private set; } = new();
    public WindowState Window { get; private set; } = new();

    /// Iets dat de app niet kon opslaan. Kan `null` zijn, en het is dan niet waar
    /// — een schermvulling die in het niets loopt is erger dan geen.
    public string? Warning
    {
        get => _warning;
        private set
        {
            _warning = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? Changed;

    public sealed class WindowState
    {
        [JsonPropertyName("width")] public double Width { get; set; } = 1360;
        [JsonPropertyName("height")] public double Height { get; set; } = 860;
        [JsonPropertyName("x")] public double? X { get; set; }
        [JsonPropertyName("y")] public double? Y { get; set; }
    }

    public void Load(string folder)
    {
        _file = Path.Combine(folder, "state.json");
        _spare = Path.Combine(folder, "state.backup.json");
        _cookieFile = Path.Combine(folder, "cookie.bin");

        // Van de twee bestanden wint het dat het laatst is geschreven. Er is
        // normaal maar één; het tweede wordt gebruikt als iets weigert de app te
        // laten schrijven naar het eerste, en het nieuwere van de twee lezen
        // betekent dat de plek in een boek nooit uit een verouderde kop komt.
        foreach (var candidate in new[] { _file, _spare }
                     .Where(File.Exists)
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                // Een byte-order mark uit een tekstverwerker is geen geldige JSON,
                // en het is niet de moeite waard daar de instellingen aan te
                // verliezen
                var raw = File.ReadAllText(candidate, Encoding.UTF8).TrimStart('﻿');
                var doc = JsonSerializer.Deserialize<JsonElement>(raw);
                Take(doc);
                return;
            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
            {
                // geen bestand, of een dat niet te lezen is: probeer het volgende
            }
        }
    }

    private void Take(JsonElement doc)
    {
        if (doc.ValueKind != JsonValueKind.Object) return;

        if (doc.TryGetProperty("baseUrl", out var url) && url.ValueKind == JsonValueKind.String)
            BaseUrl = url.GetString() ?? DefaultBaseUrl;
        if (doc.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.String)
            User = user.GetString() ?? "";
        if (doc.TryGetProperty("volume", out var vol) && vol.ValueKind == JsonValueKind.Number)
            Volume = Math.Clamp(vol.GetDouble(), 0, 1);
        if (doc.TryGetProperty("muted", out var muted) && muted.ValueKind is JsonValueKind.True or JsonValueKind.False)
            Muted = muted.GetBoolean();
        if (doc.TryGetProperty("openGenres", out var genres) && genres.ValueKind == JsonValueKind.Array)
            OpenGenres = genres.EnumerateArray()
                .Where(g => g.ValueKind == JsonValueKind.String)
                .Select(g => g.GetString()!)
                .ToList();
        if (doc.TryGetProperty("window", out var window) && window.ValueKind == JsonValueKind.Object)
        {
            var w = JsonSerializer.Deserialize<WindowState>(window.GetRawText()) ?? new WindowState();
            // een venster dat op een scherm staat dat er niet meer is, of kleiner
            // dan het scherm waar het op verscheen, is een venster dat niemand
            // meer kan zien of verplaatsen
            Window = ClampToScreen(w);
        }
    }

    private static WindowState ClampToScreen(WindowState w)
    {
        w.Width = Math.Clamp(w.Width, 640, 10000);
        w.Height = Math.Clamp(w.Height, 480, 10000);
        if (w.X is null || w.Y is null) return w;
        var onScreen = System.Windows.SystemParameters.VirtualScreenWidth
                       > 0
            && w.X > System.Windows.SystemParameters.VirtualScreenLeft - 200
            && w.Y > System.Windows.SystemParameters.VirtualScreenTop - 200
            && w.X < System.Windows.SystemParameters.VirtualScreenLeft + System.Windows.SystemParameters.VirtualScreenWidth
            && w.Y < System.Windows.SystemParameters.VirtualScreenTop + System.Windows.SystemParameters.VirtualScreenHeight;
        if (!onScreen)
        {
            w.X = null;
            w.Y = null;
        }
        return w;
    }

    /// Een omgevingsvariabele wint van het bewaarde adres, zodat de app voor een
    /// proef naar een andere collectie kan wijzen. Die staat bewust niet in de
    /// instellingen: een eenmalige afwijking moet niet het adres worden dat de app
    /// voor altijd gebruikt.
    public string EffectiveBaseUrl
    {
        get
        {
            var over = Environment.GetEnvironmentVariable("MABC_URL");
            if (!string.IsNullOrWhiteSpace(over)) return Trim(over);
            return Trim(BaseUrl);
        }
    }

    private static string Trim(string url) => url.TrimEnd('/');

    public void SetBaseUrl(string url)
    {
        BaseUrl = Trim(url);
        Write();
    }

    public void SetUser(string user)
    {
        User = user;
        Write();
    }

    public void SetSound(double volume, bool muted)
    {
        Volume = Math.Clamp(volume, 0, 1);
        Muted = muted;
        Write();
    }

    public void SetOpenGenres(List<string> genres)
    {
        OpenGenres = genres;
        Write();
    }

    public void SetWindow(WindowState window)
    {
        Window = window;
        Write();
    }

    /// De sessiecookie van de collectie-server, versleuteld met de sleutel van deze
    /// Windows-gebruiker.
    ///
    /// In de Electron-versie bewaarde Chromium dat zelf, in het profiel van de app.
    /// Hier is er geen Chromium, dus de app doet het zelf — en wel versleuteld,
    /// want dit bestand is leesbaar voor iedereen die het kan openen en het
    /// wachtwoord is hier niet bij. `CurrentUser` betekent dat alleen dezelfde
    /// gebruiker het kan lezen.
    public string? ReadCookie()
    {
        try
        {
            if (!File.Exists(_cookieFile)) return null;
            var cipher = File.ReadAllBytes(_cookieFile);
            if (cipher.Length == 0) return null;
            var plain = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
            var text = Encoding.UTF8.GetString(plain);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception e) when (e is CryptographicException or IOException or UnauthorizedAccessException)
        {
            // Een cookie die niet te lezen is, is hetzelfde als geen cookie: de app
            // meldt zich gewoon opnieuw aan. Zeggen wat hier misging voegt niets
            // toe voor iemand die alleen iets wil luisteren.
            return null;
        }
    }

    public void WriteCookie(string value)
    {
        try
        {
            var plain = Encoding.UTF8.GetBytes(value);
            File.WriteAllBytes(_cookieFile, ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception e) when (e is CryptographicException or IOException or UnauthorizedAccessException)
        {
            Warning = I18n.T("noSaving") + " (" + e.Message + ")";
        }
    }

    public void ForgetCookie()
    {
        try { if (File.Exists(_cookieFile)) File.Delete(_cookieFile); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* vergeten is al bijna vergeten */ }
    }

    /// Een herkenning van deze app, zodat een cookie van een ander programma op
    /// dezelfde computer niet hier terechtkomt.
    private static readonly byte[] Entropy = "mijn-audioboeken"u8.ToArray();

    private void Write()
    {
        if (_file.Length == 0) return;
        var data = new
        {
            baseUrl = BaseUrl,
            user = User,
            volume = Volume,
            muted = Muted,
            openGenres = OpenGenres,
            window = Window,
        };
        try
        {
            WriteTo(_file, data);
            Warning = null;
            return;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Iets buiten de app weigert daar te laten schrijven: een
            // antivirusprogramma dat bestanden in quarantaine zet die het niet
            // herkent, of een map waarvan de rechten veranderd zijn. De plek in
            // een boek daarom kwijtraken zou jammer zijn, dus de instellingen gaan
            // naar een tweede bestand in dezelfde map en worden de volgende keer
            // daarvandaan gelezen.
        }

        try
        {
            WriteTo(_spare, data);
            Warning = "state.json kan niet worden geschreven; de instellingen staan nu in "
                      + Path.GetFileName(_spare) + " totdat dat pad weer schrijfbaar is.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Niets meer om te proberen. De app blijft werken; alleen de plek waar
            // je was gaat verloren, en dat wordt gezegd in plaats van te doen alsof
            // het onbelangrijk is.
            Warning = I18n.T("noSaving") + " (" + e.Message + ")";
        }
    }

    private static void WriteTo(string where, object data)
    {
        // Naar een buurbestand en dan omhoog: een app die tijdens het schrijven
        // omvalt laat een half bestand achter in plaats van geen, en een half
        // bestand wordt bij de volgende start genegeerd.
        var tmp = where + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, Json), Encoding.UTF8);
        try
        {
            File.Move(tmp, where, overwrite: true);
        }
        catch
        {
            // Het nieuwe bestand is op zichzelf voor niemand goed, en het laten
            // staan maakt de volgende start alleen maar twijfelen welke van de twee
            // de echte is.
            try { File.Delete(tmp); } catch { /* en als ook dat geweigerd wordt, is het één onschadelijk bestand */ }
            throw;
        }
    }
}
