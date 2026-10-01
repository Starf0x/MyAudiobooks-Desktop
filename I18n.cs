using System.Globalization;
using System.Text.RegularExpressions;

namespace MyAudiobooks;

/// De teksten van de app, in het Nederlands en in het Engels.
///
/// Eén plek, omdat de vertaling overal tegelijk mee moet bewegen: een tekst die
/// op drie plekken in twee talen staat staat op vier plekken fout zodra er één bij
/// komt.
///
/// De twee tabellen zijn kopieën van elkaar. Dat is hier een bewuste keuze en
/// geen slordigheid: het zijn losse woordenlijsten, geen regels code, en een
/// samenvoegde tabel met een taalselector zou bij het toevoegen van een tekst
/// net zo goed kunnen, maar dan moet elke schrijver eraan denken. Hier staat de
/// Nederlandse tekst boven de Engelse en is het verschil in één oogopslag te
/// zien. De regel is: een sleutel die in de ene tabel staat, staat ook in de
/// andere, en wordt ook gebruikt. Een dode sleutel is een tekst die ooit nodig
/// was en nu niemand meer laat zien.
///
/// Welke taal het wordt beslist het besturingssysteem, en dat wordt nergens
/// bewaard. Wie zijn taal omzet in Windows ziet dit venster meteen in die taal,
/// en de app onthoudt daar niets over: een venster dat zijn taal onthoudt is een
/// venster dat in het Engels blijft staan op een Nederlandse computer.
public static class I18n
{
    private static readonly Dictionary<string, string> Nl = new()
    {
        // het adres waar de vertaling voor geldt; het bepaalt ook hoe de
        // puntjes tussen de duizendtallen eruitzien
        ["locale"] = "nl-NL",

        ["appName"] = "Mijn Audioboeken",
        ["start"] = "Start",
        ["backToShelves"] = "Terug naar de planken",
        ["backToSignIn"] = "Terug",
        ["search"] = "Zoek op titel, auteur, serie…",
        ["searching"] = "Zoeken",
        ["signOut"] = "Afmelden",
        ["loading"] = "Even geduld…",

        ["genres"] = "Genres",
        ["authors"] = "Auteurs",
        ["favourites"] = "Favorieten",
        ["listened"] = "Beluisterd",
        ["noAuthors"] = "Geen auteurs.",
        ["pickAuthor"] = "Kies een auteur.",
        ["noBooks"] = "Geen boeken.",
        ["nothingMatches"] = "Niets past bij \"{q}\".",
        ["noHearts"] = "Nog geen hartjes. Het hartje op een boek zet het hier neer.",
        ["noFavouritesHere"] = "Deze collectie kent geen favorieten.",
        ["inYourFavourites"] = "In je favorieten",
        ["addToFavourites"] = "Bij je favorieten voegen",
        ["favouriteAdded"] = "“{title}” staat nu bij je favorieten.",
        ["favouriteRemoved"] = "“{title}” is van je favorieten af.",

        ["statBooks"] = "audioboeken",
        ["statFiles"] = "bestanden",
        ["statDone"] = "af",
        ["continueListening"] = "Verder luisteren",
        ["addedRecently"] = "Onlangs toegevoegd",

        ["synopsis"] = "Synopsis",
        ["noDescription"] = "Geen beschrijving.",
        ["parts"] = "Delen",
        ["partOf"] = "deel {n}/{t} · {title}",
        ["partNo"] = "deel {n}",
        ["part"] = "deel {n} van {t}",
        ["partsOf"] = "{n} delen",
        ["oneFile"] = "één bestand",
        ["noParts"] = "Dit boek heeft geen audiobestanden.",
        ["series"] = "Serie",
        ["book"] = "boek {n}",
        ["narrator"] = "Verteller: {name}",
        ["whereYouWere"] = "Je was bij {where}",
        ["whereYouHere"] = "hier {time}",
        ["play"] = "▶ Afspelen",
        ["resume"] = "▶ Hervatten",
        ["again"] = "▶ Opnieuw",
        ["homeShort"] = "Thuis",
        ["backHome"] = "Terug naar thuis vanuit {name}",
        ["books"] = "Boeken",
        ["nothingYet"] = "Hier staat nog niets. Kies een genre links.",
        ["nothingToContinue"] = "Nog niets om verder te luisteren. Zet een boek op en het komt hier te staan.",
        ["searchResults"] = "Gevonden",
        ["foldSeries"] = "De series van {name} klappen",
        ["seriesUnder"] = "{n} series onder dit genre",
        ["playShort"] = "▶ Afspelen",
        ["resumeShort"] = "▶ Hervatten",
        ["playThis"] = "{t} afspelen",
        ["resumeThis"] = "{t} hervatten",
        ["startThis"] = "{t} opnieuw",
        ["homePlanks"] = "de planken op de thuispagina",
        ["shelfMissing"] = "{name} zijn er niet: {why}",
        ["listenedShelf"] = "de lijst met beluisterde boeken",
        ["playIt"] = "Afspelen",
        ["playPart"] = "Speel {t}",
        ["pauseIt"] = "Pauzeren",
        ["pressPlay"] = "Druk op ▶ om verder te gaan.",
        ["finished"] = "Beluisterd",
        ["positionIn"] = "Positie in dit deel",

        ["prevPart"] = "Vorig deel",
        ["nextPart"] = "Volgend deel",
        ["back15"] = "15 seconden terug",
        ["forward30"] = "30 seconden vooruit",
        ["playOrPause"] = "Afspelen of pauzeren",
        ["closePlayer"] = "De speler wegzetten",
        ["sound"] = "Geluid aan of uit",
        ["soundOff"] = "Geluid uit",
        ["soundOn"] = "Geluid aan",
        ["volume"] = "Volume",
        ["sleepTimer"] = "Slaaptimer",
        ["sleepTimerStop"] = "Zet het geluid over {n} minuten uit",
        ["sleepTimerStopNow"] = "Slaaptimer afzeggen",
        ["sleepTimerSet"] = "Slaaptimer gezet: het geluid gaat over {n} minuten uit.",
        ["sleepTimerDone"] = "Slaaptimer af: het geluid is uitgezet.",

        ["gateTitle"] = "Mijn Audioboeken",
        ["gateHint"] = "Meld je aan om verder te gaan waar je was. Waar je in een boek "
            + "gebleven bent, en welke boeken af zijn, worden per persoon bewaard.",
        ["name"] = "Naam",
        ["password"] = "Wachtwoord",
        ["signIn"] = "Aanmelden",
        ["signInFailed"] = "Naam of wachtwoord klopt niet.",
        ["bothFields"] = "Beide, alsjeblieft.",
        ["otherAddress"] = "Ander adres",
        ["address"] = "Adres",
        ["addressHint"] = "Het adres van je collectie. Standaard is de website hierboven; "
            + "verander het alleen als je de collectie zelf ergens draait.",
        ["addressMustStart"] = "Een adres begint met https://",
        ["save"] = "Opslaan",

        // De app speelt alleen mp3, en daarom staat er bij elke mislukking zowel
        // wat er misging als dát er maar één formaat kan. Een speler die stil
        // blijft zonder iets te zeggen is erger dan een die zegt dat hij het niet
        // kan, en "het is geen mp3 en deze app speelt geen m4a" is een reden die
        // iemand kan begrijpen en er iets aan kan doen.
        ["onlyMp3"] = "Deze app speelt alleen mp3.",
        ["converting"] = "Omzetten naar mp3, {done} van {total} minuten…",
        ["convertingStart"] = "Omzetten naar mp3 begint…",
        ["convertingFailed"] = "Dit deel kon niet naar mp3 worden omgezet: {name}",
        // Deze twee regels staan nooit alleen op het scherm. Ze komen pas te
        // voorschijn nadat `/api/stream` ook niets gaf, en dan volgen ze op
        // `noStream`. Ze zeggen dus niet "er is geen andere manier om dit te
        // horen" — dat zou op dit punt onwaar zijn, want de app heeft de
        // andere manier al geprobeerd. Ze zeggen wat de eerste vraag opleverde,
        // want dat is het stuk van de waarheid dat `noStream` niet bevat.
        ["noMp3Route"] = "De collectieserver heeft geen mp3-route, de route waar de "
            + "webinterface de mp3 maakt. Wat hij daar antwoordde: {said}",
        ["noMp3RouteNothingSaid"] = "De collectieserver heeft geen mp3-route, de route "
            + "waar de webinterface de mp3 maakt. Hij zei er niets over; in "
            + "verzoeken.log staat wat hij wel antwoordde.",
        ["noSoundFile"] = "Dit audiobestand kon niet worden geladen.",
        ["noStream"] = "De server gaf dit deel ook niet: {said}",
        ["notMp3"] = "Dit deel is geen mp3 — de server stuurde {kind}. Deze app speelt "
            + "alleen mp3. In de webinterface kun je het boek omzetten met Omzetten "
            + "naar mp3, en daarna speelt dit deel hier ook.",
        ["couldNotPlay"] = "Het kon niet verder spelen: {name}",
        ["noSound"] = "Het geluid speelt niet af ({name}).",
        ["positionBehind"] = "De plek waar je was lag voorbij het einde van dit deel; "
            + "het begint opnieuw ({name}).",

        ["savePlaceFailed"] = "Je voortgang staat nergens, want de server bewaarde hem niet: {name}",
        ["noSaving"] = "Deze app speelt alleen af; er wordt niets bewaard.",

        ["unreachable"] = "De collectie-server antwoordt niet. Controleer of hij draait, "
            + "en of het adres klopt.",
        ["noAnswer"] = "geen antwoord",
        ["notForYou"] = "Dit adres hoort bij een ander programma.",
        ["notFound"] = "Niet gevonden",
        ["serverSaid"] = "De collectie-server zei: {name}",
        ["unreadable"] = "Op {where} gaf de collectie-server een antwoord dat niet te lezen is"
                       + " (status {code}): {body}",
        ["tooMuch"] = "Te veel gegevens in één verzoek.",
    };

    private static readonly Dictionary<string, string> En = new()
    {
        ["locale"] = "en-GB",

        ["appName"] = "My Audiobooks",
        ["start"] = "Home",
        ["backToShelves"] = "Back to the shelves",
        ["backToSignIn"] = "Back",
        ["search"] = "Search by title, author, series…",
        ["searching"] = "Searching",
        ["signOut"] = "Sign out",
        ["loading"] = "One moment…",

        ["genres"] = "Genres",
        ["authors"] = "Authors",
        ["favourites"] = "Favourites",
        ["listened"] = "Finished",
        ["noAuthors"] = "No authors.",
        ["pickAuthor"] = "Pick an author.",
        ["noBooks"] = "No books.",
        ["nothingMatches"] = "Nothing matches \"{q}\".",
        ["noHearts"] = "No hearts yet. The heart on a book puts it here.",
        ["noFavouritesHere"] = "This collection has no favourites.",
        ["inYourFavourites"] = "In your favourites",
        ["addToFavourites"] = "Add to your favourites",
        ["favouriteAdded"] = "“{title}” is now in your favourites.",
        ["favouriteRemoved"] = "“{title}” is out of your favourites.",

        ["statBooks"] = "audiobooks",
        ["statFiles"] = "files",
        ["statDone"] = "done",
        ["continueListening"] = "Carry on listening",
        ["addedRecently"] = "Added recently",

        ["synopsis"] = "Synopsis",
        ["noDescription"] = "No description.",
        ["parts"] = "Parts",
        ["partOf"] = "part {n}/{t} · {title}",
        ["partNo"] = "part {n}",
        ["part"] = "part {n} of {t}",
        ["partsOf"] = "{n} parts",
        ["oneFile"] = "one file",
        ["noParts"] = "This book has no audio files.",
        ["series"] = "Series",
        ["book"] = "book {n}",
        ["narrator"] = "Narrated by {name}",
        ["whereYouWere"] = "You were at {where}",
        ["whereYouHere"] = "here {time}",
        ["play"] = "▶ Play",
        ["resume"] = "▶ Resume",
        ["again"] = "▶ Again",
        ["homeShort"] = "Home",
        ["backHome"] = "Back to home from {name}",
        ["books"] = "Books",
        ["nothingYet"] = "Nothing here yet. Pick a genre on the left.",
        ["nothingToContinue"] = "Nothing to carry on with yet. Start a book and it will be here.",
        ["searchResults"] = "Found",
        ["foldSeries"] = "Fold the series of {name}",
        ["seriesUnder"] = "{n} series under this genre",
        ["playShort"] = "▶ Play",
        ["resumeShort"] = "▶ Resume",
        ["playThis"] = "Play {t}",
        ["resumeThis"] = "Resume {t}",
        ["startThis"] = "{t} from the start",
        ["homePlanks"] = "the shelves on the home page",
        ["shelfMissing"] = "{name} are not there: {why}",
        ["listenedShelf"] = "the list of finished books",
        ["playIt"] = "Play",
        ["playPart"] = "Play {t}",
        ["pauseIt"] = "Pause",
        ["pressPlay"] = "Press ▶ to carry on.",
        ["finished"] = "Finished",
        ["positionIn"] = "Position in this part",

        ["prevPart"] = "Previous part",
        ["nextPart"] = "Next part",
        ["back15"] = "Back 15 seconds",
        ["forward30"] = "Forward 30 seconds",
        ["playOrPause"] = "Play or pause",
        ["closePlayer"] = "Close the player",
        ["sound"] = "Sound on or off",
        ["soundOff"] = "Sound off",
        ["soundOn"] = "Sound on",
        ["volume"] = "Volume",
        ["sleepTimer"] = "Sleep timer",
        ["sleepTimerStop"] = "Turns the sound off in {n} minutes",
        ["sleepTimerStopNow"] = "Cancel sleep timer",
        ["sleepTimerSet"] = "Sleep timer set: the sound goes off in {n} minutes.",
        ["sleepTimerDone"] = "Sleep timer done: the sound is off.",

        ["gateTitle"] = "My Audiobooks",
        ["gateHint"] = "Sign in to carry on where you were. Where you got to in a book, "
            + "and which books are finished, are kept per person.",
        ["name"] = "Name",
        ["password"] = "Password",
        ["signIn"] = "Sign in",
        ["signInFailed"] = "Name or password is not right.",
        ["bothFields"] = "Both of them, please.",
        ["otherAddress"] = "Other address",
        ["address"] = "Address",
        ["addressHint"] = "The address of your collection. The website above is the "
            + "default; only change it if you run the collection yourself.",
        ["addressMustStart"] = "An address starts with https://",
        ["save"] = "Save",

        ["onlyMp3"] = "This app only plays mp3.",
        ["converting"] = "Converting to mp3, {done} of {total} minutes…",
        ["convertingStart"] = "Converting to mp3 is starting…",
        ["convertingFailed"] = "This part could not be converted to mp3: {name}",
        ["noMp3Route"] = "The collection server has no mp3 route, the one where the web "
            + "interface renders the mp3. What it answered there: {said}",
        ["noMp3RouteNothingSaid"] = "The collection server has no mp3 route, the one where "
            + "the web interface renders the mp3. It said nothing about it; verzoeken.log "
            + "has what it did answer.",
        ["noSoundFile"] = "This audio file could not be loaded.",
        ["noStream"] = "The server would not give this part either: {said}",
        ["notMp3"] = "This part is not mp3 — the server sent {kind}. This app plays "
            + "only mp3. In the web interface you can convert the book with "
            + "Convert to MP3, and after that this part plays here too.",
        ["couldNotPlay"] = "It could not carry on: {name}",
        ["noSound"] = "The sound did not start ({name}).",
        ["positionBehind"] = "The place you were at lay past the end of this part; "
            + "it starts again ({name}).",

        ["savePlaceFailed"] = "Your progress is stored nowhere, because the server did not store it: {name}",
        ["noSaving"] = "This app only plays; nothing is saved.",

        ["unreachable"] = "The collection server is not answering. Check that it is "
            + "running, and that the address is right.",
        ["noAnswer"] = "no answer",
        ["notForYou"] = "This address belongs to another program.",
        ["notFound"] = "Not found",
        ["serverSaid"] = "The collection server said: {name}",
        ["unreadable"] = "On {where} the collection server returned an answer that cannot be read"
                       + " (status {code}): {body}",
        ["tooMuch"] = "Too much data in one request.",
    };

    private static readonly Regex Hole = new(@"\{(\w+)\}", RegexOptions.Compiled);

    private static readonly Dictionary<string, Dictionary<string, string>> Langs = new()
    {
        ["nl"] = Nl,
        ["en"] = En,
    };

    /// De taal van dit venster, uit de taal van het besturingssysteem.
    public static string Current { get; private set; } = "nl";

    /// "nl-NL" en "nl" worden Nederlands; ieder ander besturingssysteem krijgt
    /// Engels, want Nederlands is geen taal die je zomaar aan iemand anders geeft.
    /// Alleen als er helemaal geen taal bekend is, valt het terug op Nederlands.
    public static string Pick(string? tag)
    {
        var language = (tag ?? "").Split('-')[0].ToLowerInvariant();
        if (language.Length == 0) return "nl";
        return Langs.ContainsKey(language) ? language : "en";
    }

    public static void SetFromSystem()
    {
        Current = Pick(CultureInfo.CurrentUICulture.Name);
    }

    /// De volledige taalcode, voor de puntjes tussen de duizendtallen: 1.234 in
    /// het Nederlands en 1,234 in het Engels.
    public static string Locale => Langs[Current]["locale"];

    /// Een tekst uit deze taal. {n} en {name} worden ingevuld; een sleutel die
    /// ontbreekt geeft de sleutel terug, zodat een ontbrekende vertaling zichtbaar
    /// is in plaats van stil te verdwijnen.
    public static string T(string key, params (string Name, object? Value)[] vars)
    {
        var table = Langs[Current];
        if (!table.TryGetValue(key, out var text)) return key;
        return Fill(text, vars);
    }

    public static string Number(long value) =>
        value.ToString("N0", CultureInfo.GetCultureInfo(Locale));

    private static string Fill(string text, (string Name, object? Value)[] vars)
    {
        if (vars.Length == 0) return text;
        return Hole.Replace(text, m =>
        {
            foreach (var (name, value) in vars)
                if (name == m.Groups[1].Value) return value?.ToString() ?? "";
            return m.Value;
        });
    }
}
