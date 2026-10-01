using System.Windows;
using System.Windows.Threading;

namespace MyAudiobooks;

/// De start van het programma.
///
/// Er is één regel hier die er echt toe doet: niets van wat hier gebeurt mag
/// stil falen. Een app die bij het opstarten een venster opent en dan in
/// stilte vastloopt, is het ergste dat deze app kan doen — dan is er geen
/// venster, geen geluid, en geen enkele aanwijzing waarom. Dus elk ongehandeld
/// misverstand gaat naar een venster met de reden erin.
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // De taal van het besturingssysteem, een keer, bij het opstarten. Niet
        // bewaard: wie zijn Windows op een andere taal zet, ziet dit venster in
        // die taal, en dat is wat hij ook in elke ander programma ziet.
        I18n.SetFromSystem();

        DispatcherUnhandledException += OnTrouble;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Show(args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            // Een vergeten taak is geen ramp voor iemand die iets wil luisteren,
            // maar het wel zichtbaar zeggen kost niets en verbergt geen fout.
            args.SetObserved();
            Show(args.Exception);
        };

        var store = new Store();
        store.Load(Where());

        // De lijst van de vorige start weg, zodat `verzoeken.log` alleen zegt
        // wat er in deze start is gebeurd.
        ForgetRequests();

        var window = new MainWindow(store);
        MainWindow = window;
        window.Show();
    }

    /// Waar de app zijn eigen bestanden bewaart.
    ///
    /// Standaard de map van de app, niet de roaming map van de gebruiker. Dit is
    /// een programma voor één persoon op één computer, en in de roaming map zou
    /// de plek in een boek mee naar een andere computer gaan — waar hij, zonder
    /// de collectie erbij, alleen maar in de weg staat.
    ///
    /// En als die map niet schrijfbaar is, dan een naast het programma zelf.
    /// Dat is de map waar het programma sowieso in staat, want je kunt een exe
    /// alleen starten als je map al bestaat. `Store` heeft daar nog een derde
    /// bestand voor, en zegt het als ook dat niet lukt.
    ///
    /// Eén keer bepaald, niet bij elke aanroep. Het proefbestand dat hieronder
    /// ontstaat en verdwijnt is namelijk zichtbaar voor iedereen die tegelijk
    /// meekijkt: twee draadjes die tegelijk bellen kunnen elkaars proef zien
    /// verdwijnen, en het ene draadje concludeert dan dat de map niet
    /// schrijfbaar is. Zo belandde de voortgang in LocalAppData en de
    /// foutenlijst ernaast in de appmap, en had deze app ineens twee plekken
    /// waar ze haar eigen bestanden bewaart. Eén beslissing, één map.
    public static string Where() => _where ??= Choose();

    private static string? _where;

    private static string Choose()
    {
        // Een map die een proefje aanwijst, en alleen die. `MABC_DATA` staat niet
        // in de gebruiksaanwijzing, want er is geen gebruik voor behalve de
        // proefjes, en daar is de reden juist belangrijk: de proefjes zetten hun
        // eigen proefserver op, en zonder deze variabele zouden ze iedere ronde
        // de cookie en de voortgang van de echte installatie overschrijven. De
        // gebruiker zou zich na elke proefronde opnieuw aanmelden zonder te
        // weten waarom. Dat is geen testprobleem maar een probleem van de
        // gebruiker, en het hoort dus niet in zijn echte map.
        var gezet = System.Environment.GetEnvironmentVariable("MABC_DATA");
        if (!string.IsNullOrWhiteSpace(gezet))
        {
            if (CanWrite(gezet)) return gezet;

            // Niet stilletjes ergens anders heen, want dan schrijft een proefje
            // zijn cookie in de map van de echte app en is het probleem juist
            // groter geworden, en niemand zegt waarom.
            throw new InvalidOperationException(
                $"MABC_DATA staat op {gezet} en daar kan niet in worden geschreven. "
                + "Zet MABC_DATA op een map waar dit account mag schrijven, of "
                + "haal de variabele weg.");
        }

        var here = System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "My Audiobooks");
        if (CanWrite(here)) return here;

        var beside = System.IO.Path.Combine(AppContext.BaseDirectory, "data");
        try
        {
            System.IO.Directory.CreateDirectory(beside);
            if (CanWrite(beside)) return beside;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // en dan is er geen map die werkt; dat zegt `Store` vanavond nog
        }
        return AppContext.BaseDirectory;
    }

    private static bool CanWrite(string folder)
    {
        // Een naam met het nummer van deze draad in, zodat twee draadjes elkaar
        // nooit in de weg zitten. De uitkomst is dan misschien in een keer niet
        // helemaal zuiver — een map die net op dit moment dichtgaat — maar het
        // antwoord is wel hetzelfde voor iedereen, en dat is wat telt.
        var probe = System.IO.Path.Combine(
            folder, $".schrijfproef-{Environment.CurrentManagedThreadId}");
        try
        {
            System.IO.Directory.CreateDirectory(folder);
            System.IO.File.WriteAllText(probe, "");
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            try { System.IO.File.Delete(probe); } catch (Exception) { /* en dan laat het maar */ }
        }
    }

    private void OnTrouble(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Show(e.Exception);

        // Het venster bestat er nog, en dan is doorsparen verstandig: iemand die
        // een boek wil luisteren wil niet dat het programma sluit omdat er iets
        // onbelangrijks misging.
        e.Handled = true;

        // Maar alleen als er inderdaad iets is waar je iets mee kunt doen. Een
        // misverstand dat het hoofdenster nooit heeft opgeleverd en dan toch
        // doorlopen laat een proces achter zonder venster, zonder taakbalkitem
        // en zonder uitleg — precies het stille mislukken dat deze app wil
        // vermijden, en erger dan gewoon niet starten. Zo'n app gaat dus weg,
        // met de reden in het venster dat zojuist verscheen en in `fouten.log`.
        if (MainWindow is null || !MainWindow.IsVisible) Shutdown();
    }

    private static void Show(Exception? why)
    {
        // Eerst op schijf, dan in een venster. Een meldingsvenster kan niet
        // verschijnen — geen bureaublad, een sessie zonder vensters, of de
        // melding zelf is de fout — en dan zou de fout weg zijn zonder dat
        // iemand hem gezien heeft. In het bestand staat hij wel.
        Note(why);

        try
        {
            MessageBox.Show(
                $"{I18n.T("appName")}\n\n{Explain(why)}",
                I18n.T("appName"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception)
        {
            // Er is niets meer om mee te zeggen: het meldingsvenster zelf komt
            // niet meer op. Dan is er geen app om mee te klagen.
        }
    }

    /// De fout, en alles wat eronder zit.
    ///
    /// Dit is niet cosmetiek. De buitenste fout van bijvoorbeeld XAML zegt
    /// alleen "set property Style threw an exception"; wie het niet verder
    /// leest, weet niet dat er een stijl is die een eigenschap probeert te
    /// zetten die dat element niet heeft. De keten is dus de fout, en een
    /// melding die bij de eerste regel stopt liegt vooral over zichzelf.
    ///
    /// En bij XAML staat de regelnummer- en kolominformatie in losse
    /// eigenschappen in plaats van in de tekst, dus die komen er apart bij.
    private static string Explain(Exception? why)
    {
        if (why is null) return "?";

        var lines = new System.Collections.Generic.List<string>();
        for (var e = why; e is not null; e = e.InnerException)
        {
            var where = e is System.Windows.Markup.XamlParseException x
                && x.LineNumber > 0
                ? $" (regel {x.LineNumber}, kolom {x.LinePosition})"
                : "";
            lines.Add($"{e.GetType().Name}{where}: {e.Message}");

            // Een keten van twintig is geen uitleg meer maar een muur; de eerste
            // paar regels zeggen wat er mis is, de rest is gevolg.
            if (lines.Count == 5)
            {
                lines.Add("…");
                break;
            }
        }
        return string.Join("\n\n", lines);
    }

    /// Dezelfde fout, met de hele stapelspoor, in `fouten.log`.
    ///
    /// `ToString()` geeft de hele keten en zegt in welke regel het misging, wat
    /// het venster niet kan: daar staat de regel alleen voor XAML. Het bestand
    /// groeit zomaar door, en dat is de bedoeling — weggooien mag iemand anders
    /// doen, want hij weet niet wat erin staat.
    private static void Note(Exception? why)
    {
        if (why is null) return;
        try
        {
            var file = System.IO.Path.Combine(Where(), "fouten.log");
            System.IO.File.AppendAllText(
                file,
                $"--- {System.DateTime.Now:yyyy-MM-dd HH:mm:ss} ---\r\n{why}\r\n\r\n");
        }
        catch (Exception)
        {
            // Ook dit kan mislopen, en dan is er geen app meer om mee te
            // klagen. Het venster hieronder is de laatste plek waar het
            // gezegd kan worden.
        }
    }

    /// Wat er van de collectie-server binnenkomt, in `verzoeken.log`.
    ///
    /// Waarom dit er is: de app praat met een server die zij niet beheert, en
    /// als het antwoord niet klopt met wat de app verwacht, dan is de enige
    /// manier om dat te weten het antwoord zelf te hebben. Zonder dit bestand
    /// staat er alleen "de server zei: 200", en dat is een klacht die niet
    /// klopt — 200 is een geslaagde aanroep. De server had iets anders terug
    /// kunnen geven, en dan was hier meteen te zien geweest waar het aan lag.
    ///
    /// Het bestand wordt bij het opstarten weggegooid, dus het bevat alleen
    /// deze start. Daarom is de regel kort: methode, pad, status, soort
    /// antwoord, en bij iets onleesbaars de eerste letters van de body.
    public static void Trace(string line)
    {
        try
        {
            var file = System.IO.Path.Combine(Where(), "verzoeken.log");
            System.IO.File.AppendAllText(file, $"{System.DateTime.Now:HH:mm:ss.fff}  {line}\r\n");
        }
        catch (Exception)
        {
            // Een log dat niet kan worden geschreven is geen reden om de app
            // te laten stoppen met iets doen.
        }
    }

    /// De lijst van de vorige start weggooien, anders groeit het bestand tot
    /// het de schijf vol gooit. Zie `Trace`.
    public static void ForgetRequests()
    {
        try
        {
            var file = System.IO.Path.Combine(Where(), "verzoeken.log");
            if (System.IO.File.Exists(file)) System.IO.File.Delete(file);
        }
        catch (Exception)
        {
            // en dan staat het vorige bestand er nog; dat is geen ramp
        }
    }
}
