using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyAudiobooks;

/// Wat de collectie-server teruggeeft, en niets anders.
///
/// Elke klasse hier staat één keer per antwoord, met de naam van het veld erbij.
/// Ze zijn al dan niet aanwezig: `series` ontbreekt bij de meeste boeken, `narrator`
/// staat er niet op elk boek, en `progress` is `null` zolang je een boek niet
/// hebt aangezet. Ontbreken mag dus nooit een fout zijn — alleen een
/// standaardwaarde, en de schermpagina's weten wat een lege waarde betekent.
///
/// De nummers heten zoals ze heten omdat de server ze zo noemt. Ze hernoemen zou
/// het porten makkelijker lijken en het onderhoud moeilijker maken: wie dan ook
/// een antwoord van de server naast deze code legt, moet het kunnen leiden.
public static class Wire
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new SoftBool(), new NullAsZero(), new LongAsZero() },
    };

    public static T? Read<T>(string json) where T : class =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<T>(json, Options);

    /// Een waarde die uit een sqlite-kolom komt is `0` of `1`, en hetzelfde veld
    /// kan uit een stuk JavaScript komen als `true` of `false`. Het zijn allebei
    /// waar of niet waar, dus allebei gelezen worden.
    ///
    /// Zonder dit zou de app op een boek `started: 0` de helft van zijn planken
    /// stil laten: het veld zou een typefout zijn, en een typefout in JSON geeft
    /// geen halve waarheid maar een leeg scherm.
    private sealed class SoftBool : JsonConverter<bool?>
    {
        public override bool? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.True: return true;
                case JsonTokenType.False: return false;
                case JsonTokenType.Number: return reader.GetInt32() != 0;
                case JsonTokenType.String:
                    var text = reader.GetString();
                    return text switch
                    {
                        null => null,
                        "1" => true,
                        "0" => false,
                        _ => bool.TryParse(text, out var yes) ? yes : null,
                    };
                default: return null;
            }
        }

        public override void Write(Utf8JsonWriter writer, bool? value, JsonSerializerOptions options)
        {
            if (value is null) writer.WriteNullValue();
            else writer.WriteBooleanValue(value.Value);
        }
    }

    /// Een getal dat `null` is, is 0.
    ///
    /// De server is JavaScript en heeft geen getallen zonder waarde. Een boek
    /// zonder omslag of zonder duur is daar `null` en niet een getal, en in C#
    /// is `int` een veld dat geen `null` kan zijn: het lezen stopt dan met een
    /// JsonException, en omdat het de eerste keer voorkomt dat er een boek
    /// binnenkomt, valt dan de hele lijst weg. Dit gebeurde echt — de eerste
    /// aanroep van de echte server, op het eerste boek — en het zag eruit als
    /// "de server zei: 200".
    ///
    /// Er is een tweede manier geweest: elk getal `int?` maken en overal
    /// `?? 0` schrijven. Dan vergeet het er één keer een, en dat is een
    /// NullReferenceException verderop in plaats van hier een schone nul.
    ///
    /// `HandleNull` is het enige dat hier nodig is: het zegt dat deze lezer
    /// ook bij `null` wordt gevraagd. Getallen mogen alsnog als tekst komen,
    /// want sqlite levert getallen als tekst en dat is dezelfde soort
    /// verrassing als `true` in plaats van `1`.
    private sealed class NullAsZero : JsonConverter<int>
    {
        public override bool HandleNull => true;

        public override int Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Null: return 0;
                case JsonTokenType.Number: return reader.TryGetInt32(out var n) ? n : 0;
                case JsonTokenType.String:
                    var text = reader.GetString();
                    return int.TryParse(text, out var yes) ? yes : 0;
                default: return 0;
            }
        }

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
            => writer.WriteNumberValue(value);
    }

    /// Zie `NullAsZero`. Voor `long`, om dezelfde reden: het zijn getallen van
    /// de server, en die zijn `null` als de server ze niet heeft.
    private sealed class LongAsZero : JsonConverter<long>
    {
        public override bool HandleNull => true;

        public override long Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Null: return 0;
                case JsonTokenType.Number: return reader.TryGetInt64(out var n) ? n : 0;
                case JsonTokenType.String:
                    var text = reader.GetString();
                    return long.TryParse(text, out var yes) ? yes : 0;
                default: return 0;
            }
        }

        public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
            => writer.WriteNumberValue(value);
    }
}

public sealed class Book
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("author")] public string Author { get; set; } = "";
    [JsonPropertyName("genre")] public string Genre { get; set; } = "";
    [JsonPropertyName("series")] public string? Series { get; set; }
    [JsonPropertyName("series_no")] public int SeriesNo { get; set; }
    [JsonPropertyName("narrator")] public string? Narrator { get; set; }
    [JsonPropertyName("year")] public int? Year { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("cover")] public string? Cover { get; set; }

    /// Het totale aantal seconden van alle delen bij elkaar. Nul als de server het
    /// niet weet, en dan zegt de app nergens een eindtijd in plaats van er één te
    /// tonen die verzonnen is.
    [JsonPropertyName("duration")] public int Duration { get; set; }

    [JsonPropertyName("tagged")] public string? Tagged { get; set; }

    /// Een getal dat meebeweegt met de omslag, zodat een gewijzigde omslag niet de
    /// oude uit het geheugen van het programma tevoorschijn komt.
    [JsonPropertyName("coverV")] public int CoverV { get; set; }

    [JsonPropertyName("started")] public bool? Started { get; set; }
    [JsonPropertyName("finished")] public bool? Finished { get; set; }
    [JsonPropertyName("done")] public bool? Done { get; set; }

    /// Waar je in dit boek was, als het serverantwoord dat weet: het deel, de
    /// seconden in dat deel, en — over alle delen heen — hoe ver je in het boek was.
    [JsonPropertyName("track_idx")] public int? TrackIdx { get; set; }
    [JsonPropertyName("position")] public int? Position { get; set; }
    [JsonPropertyName("into")] public int? Into { get; set; }
    [JsonPropertyName("percent")] public int? Percent { get; set; }

    public bool IsFinished => Finished == true;
    public bool HasStarted => Started == true || (Position ?? 0) > 0;

    /// Heet dit boek deel van een reeks? Zo heet het op de site, en daar staat
    /// de naam van de reeks ook in cyaan onder de titel van het boek.
    public bool HasSeries => !string.IsNullOrWhiteSpace(Series);

    /// Het adres van de omslag, of `null` als dit boek er geen heeft. `null` is een
    /// antwoord, geen leegte: dan tekent de app het boek zonder omslag in plaats van
    /// een gebroken plaatje te laten staan tot de server terugkomt.
    public Uri? CoverUri(string baseUrl) =>
        string.IsNullOrWhiteSpace(Cover) ? null : new Uri($"{baseUrl}/api/cover/{Id}?v={CoverV}");
}

public sealed class Track
{
    [JsonPropertyName("id")] public long Id { get; set; }

    /// De plek van dit deel in het boek, vanaf 0. Niet de id: die is een volgnummer
    /// uit de database en zegt niets over de volgorde, terwijl de opgeslagen
    /// plek in dit getal staat.
    [JsonPropertyName("idx")] public int Idx { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("duration")] public int Duration { get; set; }
}

/// De voortgang in één boek. `null` zolang er niets bewaard is, wat een reden is
/// om "Afspelen" te zeggen en niet "Hervatten".
public sealed class KeptPlace
{
    [JsonPropertyName("track_idx")] public int TrackIdx { get; set; }
    [JsonPropertyName("position")] public int Position { get; set; }
    [JsonPropertyName("done")] public bool? Done { get; set; }
    /// Over alle delen heen: hoeveel seconden van het hele boek al klinken. En
    /// het percentage, want dat is wat de balk op een tegel laat zien. De server
    /// stuurt het ene of het andere, en niet allebei.
    [JsonPropertyName("into")] public int? Into { get; set; }
    [JsonPropertyName("percent")] public int? Percent { get; set; }
}

/// Een boek met zijn delen. Alleen `/api/books/:id` geeft dit.
public sealed class BookDetail
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("author")] public string Author { get; set; } = "";
    [JsonPropertyName("genre")] public string Genre { get; set; } = "";
    [JsonPropertyName("series")] public string? Series { get; set; }
    [JsonPropertyName("series_no")] public int SeriesNo { get; set; }
    [JsonPropertyName("narrator")] public string? Narrator { get; set; }
    [JsonPropertyName("year")] public int? Year { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("cover")] public string? Cover { get; set; }
    [JsonPropertyName("duration")] public int Duration { get; set; }
    [JsonPropertyName("coverV")] public int CoverV { get; set; }
    [JsonPropertyName("finished")] public bool? Finished { get; set; }
    [JsonPropertyName("tracks")] public List<Track> Tracks { get; set; } = new();
    [JsonPropertyName("progress")] public KeptPlace? Progress { get; set; }

    public bool IsFinished => Finished == true;
    public int Parts => Tracks.Count;
    public int TotalSeconds => Tracks.Sum(t => t.Duration);

    public Book AsBook() => new()
    {
        Id = Id, Title = Title, Author = Author, Genre = Genre, Series = Series,
        SeriesNo = SeriesNo, Narrator = Narrator, Year = Year, Description = Description,
        Cover = Cover, CoverV = CoverV, Duration = Duration, Finished = Finished,
        TrackIdx = Progress?.TrackIdx,
        Position = Progress?.Position,
        Into = Progress?.Into,
        Percent = Progress?.Percent,
        Done = Progress?.Done,
        Started = (Progress?.Position ?? 0) > 0,
    };
}

public sealed class SeriesInfo
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("author")] public string Author { get; set; } = "";
    [JsonPropertyName("books")] public int Books { get; set; }
    [JsonPropertyName("highest")] public int Highest { get; set; }

    /// De delen die er niet zijn. Leeg betekent "alles er", of "er valt niets te
    /// zeggen" als `Says` dat zegt — het onderscheid staat in de tekst.
    [JsonPropertyName("missing")] public List<int> Missing { get; set; } = new();
    [JsonPropertyName("unnumbered")] public int Unnumbered { get; set; }

    /// De zin die de server erbij schrijft. Niets is een lege string, en dan zegt
    /// de app niets: een serie van één boek is geen serie met een gat erin.
    [JsonPropertyName("says")] public string Says { get; set; } = "";
}

public sealed class SeriesHead
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("books")] public int Books { get; set; }
}

public sealed class Genre
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("books")] public int Books { get; set; }
    [JsonPropertyName("series")] public List<SeriesHead> Series { get; set; } = new();
}

public sealed class Author
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("books")] public int Books { get; set; }
}

public sealed class BooksPage
{
    [JsonPropertyName("books")] public List<Book> Books { get; set; } = new();
    [JsonPropertyName("series")] public List<SeriesInfo> Series { get; set; } = new();
}

public sealed class Home
{
    [JsonPropertyName("continue")] public List<Book> Continue { get; set; } = new();
    [JsonPropertyName("recent")] public List<Book> Recent { get; set; } = new();
}

public sealed class Stats
{
    [JsonPropertyName("books")] public long Books { get; set; }
    [JsonPropertyName("files")] public long Files { get; set; }
    [JsonPropertyName("done")] public long Done { get; set; }
    [JsonPropertyName("todo")] public long Todo { get; set; }
    [JsonPropertyName("version")] public string? Version { get; set; }
}

/// Wat de server van `/api/progress` teruggeeft: of het gelukt is, en of het boek
/// nu af is. Dat laatste beslist of je boek van de plank "verder luisteren" af gaat
/// of naar "beluisterd", en dat gebeurt aan de serverkant zodra de plek aan het
/// eind van het laatste deel staat.
public sealed class ProgressReply
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("done")] public bool? Done { get; set; }
    [JsonPropertyName("cleared")] public bool? Cleared { get; set; }
}

public sealed class Who
{
    [JsonPropertyName("user")] public string User { get; set; } = "";
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("signedIn")] public bool SignedIn { get; set; }

    /// De naam zoals de server hem noemt, of "" als er niemand aangemeld is.
    ///
    /// Twee servers, twee namen voor hetzelfde. De oudere stuurt `user`; de
    /// uitgezette stuurt `name` en zegt erbij of er iemand aangemeld is
    /// (`signedIn`), en dat antwoord is 200 ook als dat niet zo is. Allebei
    /// worden gelezen, want welke je tegenkomt is niet van tevoren te zien —
    /// en een app die alleen de eerste naam kent, laat iemand die al aangemeld
    /// is elke keer opnieuw aanmelden, en een app die alleen de tweede kent,
    /// laat iemand die dat níet is altijd binnenkomen.
    public string TheName => !string.IsNullOrWhiteSpace(User)
        ? User
        : SignedIn ? Name ?? "" : "";
}
