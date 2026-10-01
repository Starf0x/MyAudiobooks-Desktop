using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MyAudiobooks;

/// Welke dag het is, in graden â€” en de twee kleuren die daaruit komen.
///
/// Dit is een port van `day.js` van de site, woordelijk en met de reden
/// erboven: de hele app staat in Ã©Ã©n kleur op een dag, en doet er een jaar over
/// om in zijn eigen kleur terug te komen.
///
/// Drie dingen zijn overgenomen en alle drie met opzet:
///
///  - **Het getal.** `(dag * 37) % 360`. Dag nul is de dag waarop de app
///    getekend is, en dat is paars linksboven en cyaan rechtsonder. Elk
///    volgnummer draait beide tinten 37 graden, en dat is de reden dat de twee
///    tinten niet dezelfde draai hebben: zo blijft de afstand tussen de gloed
///    linksboven en de gloed rechtsonder altijd hetzelfde, en blijft de pagina
///    op elke dag dezelfde app in een andere kleur, in plaats van een
///    verschillende app.
///  - **Dag nul in lokale tijd.** `day.js` haalt de tijdzone-eruit voordat hij
///    door 86400000 deelt, en dat is geen detail: zonder dat zou het in een
///    land met een andere tijdzone een dag verschillen, en stond de pagina om
///    half twee 's nachts ineens in een andere kleur.
///  - **Verven om middernacht.** Een app die de hele nacht open blijft staan
///    hoort vanzelf mee te draaien, precies zoals een pagina die open blijft
///    staan. Daarom een klok die elke minuur kijkt of de dag veranderd is, en
///    niet een timer tot middernacht: die loopt mis bij een laptop die slaapt,
///    en dan staat het programma de hele ochtend in de kleur van gisteravond.
public static class Day
{
    /// De tint van vandaag, in graden tussen 0 en 359.
    public static int Tint(DateTime? when = null)
    {
        var now = when ?? DateTime.Now;
        // Lokale tijd, want dat is de dag die iemand beleeft. `UtcNow` zou de
        // dag verschuiven voor iedereen die niet op Greenwich staat.
        //
        // `GetTimeZoneOffset` zit op `DateTimeOffset` en niet op `DateTime`, dus
        // het verschil wordt gemaakt in twee stappen: het ogenblik met zijn
        // eigen zone, en dat zoneverschil eraf. Bij zomertijd is dat een uur, en
        // dat uur is precies waarom het niet mag worden weggelaten: zonder
        // verschuiving zou het in de zomertijd een dag achterlopen.
        var local = now - TimeZoneInfo.Local.GetUtcOffset(now);
        var days = Math.Floor((local - new DateTime(1970, 1, 1)).TotalDays);
        return (int)(days * 37 % 360);
    }

    /// Een kleur uit kleurnaam, verzadiging en lichtheid, zoals `hsl()` in CSS.
    ///
    /// Zelf geschreven omdat WPF geen HSL heeft en er geen pakket voor hoort: het
    /// is een tabel van zes regels en daarmee klaar, dus het is overleesbaarder
    /// dan een afhankelijkheid die niemand in deze app nogig heeft.
    ///
    /// Met een tabel en niet met de bekende compacte variant
    /// (`hue2rgb(p, q, h + 1/3)` en zo), want die haalt maar Ã©Ã©n keer 1 van de
    /// fractie af en ziet bij tint 314 â€” waar `h + 1/3` op 5,6 uitkomt â€” de
    /// verkeerde sector. Dat gaf een groene gloed waar een roze hoort, en een
    /// groene gloed op een dag die een roze hoort te hebben is een fout die
    /// niemand ziet: het is nog steeds een gloed, en nog steeds smooth.
    ///
    /// De tabel is van `css-color` af, en die zegt:
    ///
    ///     0Â° rood     120Â° groen    240Â° blauw
    ///    60Â° geel     180Â° cyaan     300Â° magenta
    ///
    /// en elke 60 graden schuift de twee kleuren Ã©Ã©n plaats op. Dus sector 0 is
    /// (r, x, 0) en sector 1 is (x, r, 0) en zo door, en aan het eind komt er
    /// nog het verschil tussen de gewenste lichtheid en de helft van de kleur bij.
    public static Color Hsl(double hue, double saturation, double light, byte alpha = 255)
    {
        var h = ((hue % 360) + 360) % 360;
        var s = Math.Clamp(saturation, 0, 1);
        var l = Math.Clamp(light, 0, 1);

        // `c` is de kleur met alle lichtheid eruit, `x` de tweede kleur, en `m`
        // is wat er bij op moet om de gevraagde lichtheid te krijgen. Zie de tabel
        // hierboven; dit is de ene plek waar de wiskunde van `hsl()` in staat.
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var x = c * (1 - Math.Abs(h / 60.0 % 2 - 1));
        var m = l - c / 2;

        var sector = (int)(h / 60) % 6;
        var r = sector switch { 0 or 5 => c, 1 or 4 => x, _ => 0.0 };
        var g = sector switch { 1 or 2 => c, 0 or 3 => x, _ => 0.0 };
        var b = sector switch { 2 or 3 => c, 4 or 5 => x, _ => 0.0 };

        static byte Channel(double v, double m) =>
            (byte)Math.Clamp(Math.Round((v + m) * 255), 0, 255);

        return Color.FromArgb(alpha, Channel(r, m), Channel(g, m), Channel(b, m));
    }

    /// De twee gloedjes achter de pagina, in de kleur van vandaag.
    ///
    /// Paars linksboven en cyaan rechtsonder, op dag nul. Elk dagenummer draait
    /// beide, en ze draaien evenveel, zodat de kleuren nooit in elkaar overgaan:
    /// het verschil van 64 graden blijft het verschil van 64 graden. Dat is de
    /// hele zin van het paar, en zonder het draaien per dag zou de ene in de
    /// andere kunnen vallen.
    private const double Hue1 = 252, Hue2 = 188;

    /// Schilder de twee gloedjes. `first` is linksboven, `second` rechtsonder.
    /// Schilder de twee gloedjes. `first` is linksboven, `second` rechtsonder.
    ///
    /// Rechte lijnen, en geen cirkels. Dat is een tegenspraak die eruit ziet als
    /// een tegenspraak maar het is er een: met een radiaal verlopen rekent WPF
    /// met een focusvlak, en dat vlak is geen rechthoek. Bij een middelpunt in de
    /// hoek van het venster loopt dat vlak dan buiten de vensterrand, en het
    /// resultaat is een **paarse waslaag over de hele pagina** in plaats van
    /// licht in een hoek. Dat is precies wat er gebeurde, en het is geen kleur
    /// die te zwaar was maar een vorm die zich niet liet sturen.
    ///
    /// Een rechte lijn heeft geen focusvlak, begint waar je hem zet en eindigt
    /// waar je hem zegt dat hij eindigt. Voor licht uit een hoek is dat ook het
    /// goede antwoord: het oog verwacht bij een hoek een driehoek, geen ovaal.
    ///
    /// Bij 45% van de diagonaal is het licht weg. Verder weg lijkt het mooier en
    /// is het onleesbaar: het is een achtergrond, en een achtergrond mag geen
    /// kleur hebben die eruit komt.
    public static void Paint(FrameworkElement first, FrameworkElement second, int? tint = null)
    {
        try
        {
            var day = tint ?? Tint();
            if (first is not Rectangle a || second is not Rectangle b)
            {
                App.Trace("     de kleuren van de dag niet getekend: geen rechthoeken gekregen");
                return;
            }

            a.Fill = new LinearGradientBrush
            {
                // Niet (0,0), maar een stukje de pagina in: de bovenste
                // drie procent van het venster is de titelbalk van Windows, en
                // daar valt de helderste kant van het licht anders onder.
                StartPoint = new Point(0, 0.06),
                EndPoint = new Point(1, 0.78),
                GradientStops =
                {
                    // 18% van de tint op de hoek. Meer is geen sfeer maar een
                    // kleurlaag, en daar wordt het grijs tussen de boeken van.
                    new GradientStop(Hsl(Hue1 + day, 1.0, 0.68, 0x2e), 0),
                    new GradientStop(Hsl(Hue1 + day, 1.0, 0.68, 0x00), 0.45),
                },
            };
            b.Fill = new LinearGradientBrush
            {
                // Omhoog, want de teller en de speler nemen samen ongeveer een
                // tiende van het venster in, en de onderste hoek is precies het
                // stuk dat je nooit te zien krijgt.
                //
                // Een kortere bundel dan de eerste, en niet een langere: het
                // eindpunt ligt dichterbij, dus het licht is over een kleiner
                // stuk verdeeld en dus sterker waar het staat. Met een langere
                // bundel was de tweede gloed nog geen derde zo sterk als de
                // eerste, en dan zie je één kleur en geen twee.
                StartPoint = new Point(0.86, 0.72),
                EndPoint = new Point(0.45, 0.34),
                GradientStops =
                {
                    new GradientStop(Hsl(Hue2 + day, 0.86, 0.53, 0x40), 0),
                    new GradientStop(Hsl(Hue2 + day, 0.86, 0.53, 0x00), 0.5),
                },
            };
        }
        catch (Exception e)
        {
            // Twee gloedjes zijn een uiterlijk. Lukt het schilderen niet, dan
            // blijft de achtergrond de kleur die in de XAML staat, en dat is
            // oneindig veel beter dan een programma dat niet opstart omdat het
            // zijn eigen uiterlijk niet kan tekenen.
            //
            // Maar stil zijn is hier niet goed genoeg: een app die haar eigen
            // kleuren niet tekent ziet eruit als een app die ze niet heeft. Dus
            // de reden in het log, waar iemand hem kan lezen.
            App.Trace($"     de kleuren van de dag niet getekend: {e}");
        }
    }

    /// Schilder nu, en blijf het doen zolang de dag verandert.
    public static void Watch(FrameworkElement first, FrameworkElement second)
    {
        Paint(first, second);
        var painted = Tint();
        var clock = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        clock.Tick += (_, _) =>
        {
            var now = Tint();
            if (now == painted) return;
            painted = now;
            Paint(first, second, now);
        };
        clock.Start();
    }
}