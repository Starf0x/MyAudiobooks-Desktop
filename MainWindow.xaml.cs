using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MyAudiobooks;

public partial class MainWindow : Window
{
    private enum Where_ { Home, Shelves, Book }

    private readonly Store _store;
    private readonly Collection _collection;
    private readonly Player _player;
    private readonly Covers _covers;
    private readonly CancellationTokenSource _stop = new();

    private Where_ _where = Where_.Home;
    private string? _genre;
    private string? _author;
    private string? _series;
    private List<Book>? _favourites;
    private List<Book> _listened = new();
    private List<Author> _authors = new();
    private List<Book> _continue = new();
    private HashSet<long> _hearts = new();
    private List<Book> _shown = new();
    private BookDetail? _open;
    private Where_ _bookCameFrom = Where_.Shelves;
    private Stats? _stats;
    private bool _searching;
    private string _searchFor = "";
    private int _toastNumber;
    private string? _saveWarning;

    public MainWindow(Store store)
    {
        _store = store;
        _collection = new Collection(store.EffectiveBaseUrl);
        _collection.AdoptCookie(store.ReadCookie());
        _player = new Player(_collection, store);
        _covers = new Covers(_collection.Http, _collection.BaseUrl);

        InitializeComponent();

        _player.Changed += OnPlayerChanged;
        _player.Announced += text => Dispatcher.Invoke(() => Say(text));
        _player.PartEnded += OnPartEnded;
        _player.SaveFailed += () => Dispatcher.Invoke(() =>
            Say(I18n.T("savePlaceFailed", ("name", I18n.T("noAnswer")))));
        _store.Changed += OnStoreChanged;

        Width = _store.Window.Width;
        Height = _store.Window.Height;
        Left = _store.Window.X ?? double.NaN;
        Top = _store.Window.Y ?? double.NaN;

        Translate();
    }

    // --- opstarten ----------------------------------------------------------

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Translate();

        // De achtergrondkleuren van vandaag, en ze draaien mee als de dag
        // verandert. Zie `Day`: het is een port van `day.js` van de site, met
        // hetzelfde getal en dezelfde redenen.
        Day.Watch(Glow1, Glow2);

        // Er staat een naam in de instellingen, en er is een cookie. Dat betekent
        // niet dat je nog aangemeld bent: de cookie kan van gisteren zijn, of van
        // een ander programma, of de server kan inmiddels op een andere manier
        // heten. Dus vragen, en niet aannemen.
        if (!string.IsNullOrWhiteSpace(_store.User) && _store.ReadCookie() is not null)
        {
            try
            {
                var who = await _collection.Me(_stop.Token);
                if (!string.IsNullOrEmpty(who.TheName))
                {
                    _collection.User = who.TheName;
                    _store.SetUser(who.TheName);
                    Who.Text = who.TheName;
                    SignOut.Visibility = Visibility.Visible;
                    await GoHome();
                    return;
                }
            }
            catch (Fault f) when (f.Key is "notForYou" or "unreachable")
            {
                // Twee mogelijkheden, en ze vragen om verschillend doen. Is de
                // server er niet, dan is weggooien van de cookie een straf die
                // niemand verdiend heeft: hij is er morgen misschien weer. Is je
                // cookie niet meer goed, dan is die dood en dat moet je weten.
                if (f.Key == "notForYou")
                {
                    _store.ForgetCookie();
                    _collection.ClearCookies();
                }
            }
        }

        ShowGate();
    }

    private void ShowGate()
    {
        // Eerst het scherm leeg, dan de poort ervoor. Niet de andere kant om:
        // de poort is half doorschijnend, en dan zou er een seconde lang iemands
        // genres en omslagen doorheen schemeren. Er is geen reden om daar mee te
        // wachten, en reden genoeg om het niet te doen.
        ClearThePage();
        Gate.Visibility = Visibility.Visible;
        GateName.Text = _store.User;
        GateError.Visibility = Visibility.Collapsed;
        GateName.Focus();
    }

    /// Alles van de pagina af, en wel alles.
    ///
    /// Na afmelden stonden de genres, de favorieten, de planken en de tellingen
    /// van de vorige persoon nog gewoon op het scherm, achter een half
    /// doorschijnende poort. De site laadt zichzelf opnieuw na afmelden, dus daar
    /// is er niets meer om te zien; en iedereen kan hier de poort wegklikken, dus
    /// hier wil je het niet eens kunnen zien.
    ///
    /// Ook de speler gaat weg, met zijn titel en zijn omslag. Een spelerbalk met
    /// de titel van iemands boek erin is hetzelfde lek in een andere vorm.
    private void ClearThePage()
    {
        LeftStack.Children.Clear();
        MiddleStack.Children.Clear();
        RightWrap.Children.Clear();
        HomeStack.Children.Clear();
        BookGrid.Children.Clear();
        PaneHead.Children.Clear();
        StatusStack.Children.Clear();
        LeftHead.Text = "";
        MiddleHead.Text = "";
        _authors = new();
        _favourites = null;
        _hearts.Clear();
        _listened = new();
        _shown = new();
        _open = null;
        _stats = null;
        Search.Text = "";
        _searching = false;
        _searchFor = "";
        _player.Stop();
        Bar.Visibility = Visibility.Collapsed;
        Columns.Visibility = Visibility.Collapsed;
        HomeScroll.Visibility = Visibility.Collapsed;
        ListScroll.Visibility = Visibility.Collapsed;
        BookScroll.Visibility = Visibility.Collapsed;
        PaneHome.Visibility = Visibility.Collapsed;
        Toast.Visibility = Visibility.Collapsed;
    }

    private void HideGate()
    {
        Gate.Visibility = Visibility.Collapsed;
        Columns.Visibility = Visibility.Collapsed;
        HomeScroll.Visibility = Visibility.Collapsed;
        ListScroll.Visibility = Visibility.Collapsed;
        BookScroll.Visibility = Visibility.Collapsed;
        PaneHome.Visibility = Visibility.Collapsed;
        Bar.Visibility = Visibility.Collapsed;
    }

    private async void OnSignIn(object sender, RoutedEventArgs e)
    {
        var name = GateName.Text.Trim();
        var password = GatePass.Password;
        if (name.Length == 0 || password.Length == 0)
        {
            GateError.Text = I18n.T("bothFields");
            GateError.Visibility = Visibility.Visible;
            return;
        }

        GateGo.IsEnabled = false;
        GateGo.Content = "â€¦";
        try
        {
            await _collection.SignIn(name, password, _stop.Token);
            _collection.User = name;
            _store.SetUser(name);
            // De cookie meteen wegschrijven. Zonder dit moet je bij elke start
            // opnieuw je wachtwoord typen, en dat is precies de reden dat
            // Electron het zelf deed.
            var cookie = _collection.CurrentCookie();
            if (cookie is not null) _store.WriteCookie(cookie);

            Who.Text = name;
            SignOut.Visibility = Visibility.Visible;
            Gate.Visibility = Visibility.Collapsed;
            GatePass.Clear();
            await GoHome();
        }
        catch (Fault f)
        {
            GateError.Text = f.Text;
            GateError.Visibility = Visibility.Visible;
            GatePass.Clear();
            GatePass.Focus();
        }
        finally
        {
            GateGo.IsEnabled = true;
            GateGo.Content = I18n.T("signIn");
        }
    }

    private void OnGateKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; OnSignIn(sender, e); }
    }

    private void OnSignOut(object sender, RoutedEventArgs e)
    {
        _ = _collection.SignOut(_stop.Token);
        _store.ForgetCookie();
        _collection.ClearCookies();
        _player.Stop();
        _store.SetUser("");
        Who.Text = "";
        SignOut.Visibility = Visibility.Collapsed;
        // De voortgang van de boeken blijft bij de server, en die hangt aan de
        // naam die je had. Er staat hier niets lokaals om te wissen, en dat is
        // geen verlies: de volgende die inlogt leest de voortgang van de
        // vorige terug bij de server, en nergens anders.
        ShowGate();
    }

    private void OnOtherAddress(object sender, RoutedEventArgs e)
    {
        GateAddressBox.Visibility = GateAddressBox.Visibility == Visibility.Visible
            ? Visibility.Collapsed : Visibility.Visible;
        if (GateAddressBox.Visibility == Visibility.Visible)
        {
            GateAddress.Text = _store.EffectiveBaseUrl;
            GateAddress.Focus();
            GateAddress.SelectAll();
        }
    }

    private void OnCloseAddress(object sender, RoutedEventArgs e) =>
        GateAddressBox.Visibility = Visibility.Collapsed;

    private async void OnSaveAddress(object sender, RoutedEventArgs e)
    {
        var url = GateAddress.Text.Trim();
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            GateError.Text = I18n.T("addressMustStart");
            GateError.Visibility = Visibility.Visible;
            return;
        }

        GateSave.IsEnabled = false;
        try
        {
            _store.SetBaseUrl(url);
            _collection.SetBaseUrl(url);
            var cookie = _store.ReadCookie();
            if (cookie is not null) _collection.AdoptCookie(cookie);
            GateAddressBox.Visibility = Visibility.Collapsed;
            // Opnieuw aanmelden, want het oude adres had een eigen cookie en die
            // hoort niet bij de nieuwe server.
            _store.ForgetCookie();
            _collection.ClearCookies();
            GateError.Visibility = Visibility.Collapsed;
            await _collection.Me(_stop.Token);
        }
        catch (Fault f)
        {
            GateError.Text = f.Text;
            GateError.Visibility = Visibility.Visible;
        }
        finally
        {
            GateSave.IsEnabled = true;
        }
    }

    // --- de drie plekken waar je kunt zijn -----------------------------------
    //
    // Twee, eigenlijk. De drie kolommen en de thuispagina zijn Ã©Ã©n plek geworden,
    // want de genres moeten er altijd staan en niet alleen als je aan het
    // bladeren bent. Wat wisselt is de rechterkolom: de planken van de thuispagina
    // of de lijst met boeken. De boekpagus is de andere plek, over de hele
    // breedte.
    private void Show(Where_ where)
    {
        _where = where;
        // De drie kolommen staan altijd, precies zoals op de site. Wat wisselt is
        // de rechterkolom: de planken of de lijst met boeken, en de boekpagina
        // die de kolommen dan helemaal weigert.
        Columns.Visibility = where == Where_.Book ? Visibility.Collapsed : Visibility.Visible;
        BookScroll.Visibility = where == Where_.Book ? Visibility.Visible : Visibility.Collapsed;
        HomeScroll.Visibility = where == Where_.Home ? Visibility.Visible : Visibility.Collapsed;
        ListScroll.Visibility = where == Where_.Shelves ? Visibility.Visible : Visibility.Collapsed;
        PaneHome.Visibility = where == Where_.Book ? Visibility.Collapsed : Visibility.Visible;
        PaneHome.IsEnabled = where != Where_.Home;
    }

    /// De kop boven de rechterkolom.
    ///
    /// Altijd "Books", met de "Home"-knop ernaast. Dat is letterlijk wat er in de
    /// HTML van de site staat: `<div class="col-title">â€¹ Back  Books
    /// <button id="home">Home</button></div>`, en de tekst verandert nergens. Eerst
    /// stond hier de naam van het genre of de auteur, en dan stond er bij het
    /// bladeren "Proef" en op de thuispagina "Home", met daarnaast een tweede
    /// "Home". De kop zegt niet wÃ¡t je bekijkt, maar dÃ¡t je boeken bekijkt, en de
    /// knop ernaast zegt waar je anders heen kunt. Dat is een betere splitsing dan
    /// Ã©Ã©n regel die twee dingen tegelijk moet zeggen.
    private void PaneHeadTo()
    {
        PaneHead.Children.Clear();
        var title = new TextBlock
        {
            Text = Spaced(I18n.T("books"), 0.12),
            Style = (Style)FindResource("ColTitle"),
        };
        AutomationProperties.SetName(title, I18n.T("books"));
        PaneHead.Children.Add(title);
    }

    private async Task GoHome()
    {
        Show(Where_.Home);
        PaneHeadTo();
        _genre = null;
        _author = null;
        _series = null;
        // De auteurs gaan ook weg. Ze horen bij een gekozen genre, en zonder
        // genre is "Dan Brown 1" een lijst waar niemand om vraagt.
        _authors = new();
        HomeStack.Children.Clear();
        HomeStack.Children.Add(new TextBlock
        {
            Text = I18n.T("loading"),
            Style = (Style)FindResource("Dim"),
        });

        // Elke aanroep apart, en een die misgaat kost alleen zichzelf.
        //
        // Eerst stonden ze allemaal in Ã©Ã©n try, en dan won de eerste mislukte
        // aanroep: de hele pagina werd vervangen door Ã©Ã©n regel met de klacht,
        // terwijl drie van de vier gewoon goed waren. Dat is een leeg scherm met
        // een klacht erin, en dat is precies het stille mislukken dat deze app
        // wil vermijden â€” alleen dan met een klacht erbij. Dus: wat er is komt
        // er, en wat er niet is zegt wat er niet is.
        var complaints = new List<string>();

        // Eerst de planken, en dan de linkerkant. Die volgorde is niet willekeurig:
        // de linkerrail toont de boeken die je beluisterd hebt, en die komen uit
        // hetzelfde antwoord. Anders staat die lijst er bij de eerste start nog niet
        // en pas na het bladeren, en dan is het een lijst die wisselt.
        Home? home = null;
        try { home = await _collection.Home(_stop.Token); }
        catch (Fault f) { complaints.Add(I18n.T("shelfMissing", ("name", I18n.T("homePlanks")), ("why", f.Text))); }

        // Alleen wat de server zegt. Vroeger stond hier, als de plank leeg was,
        // een boek dat deze app zelf onthouden had, en dan leek het alsof er
        // voortgang was. Maar die voortgang stond nergens anders dan in dit
        // programma, en de voortgang van een boek hoort bij de server: een
        // tweede boekhouding is een tweede waarheid, en die loopt uit de
        // eerste.
        var togo = home?.Continue ?? new List<Book>();
        _continue = togo;

        var stats = await _collection.Stats(_stop.Token).ContinueWith(
            t => t.IsFaulted ? null : t.Result, TaskScheduler.Default);
        AddStats(stats);

        // De genres staan links ook op de thuispagina. Ze worden hier apart
        // geladen, niet via `LoadGenres`, want die zou de rechterkolom leegmaken
        // en dat is precies wat je niet wilt als je net thuiskomt: eerst de
        // planken zien, en de genres hebben er de hele tijd al gestaan.
        await LoadLeftOnly();

        HomeStack.Children.Clear();
        SetColTitle(LeftHead, I18n.T("genres"));
        MiddleHead.Text = "";
        if (home is not null)
        {
            // De kop staat er altijd, ook als er niets onder staat.
            //
            // Zonder dit is het een sectie die er is zolang er iets in staat en
            // weg is zodra dat niets meer is, en dan is er geen manier om erachter
            // te komen dat die sectie bestaat. Je kijkt dan naar een pagina met een
            // rij boeken en denkt: hier staat alles, dus ook alles wat ik nog moet
            // luisteren. Dat is een app die iets verzwijgt door niets te doen.
            //
            // EÃ©n regel die zegt dat er nog niets is, kost een halve regel hoogte
            // en scheelt de vraag ernaar elke keer opnieuw.
            HomeStack.Children.Add(new TextBlock
            {
                Text = I18n.T("continueListening").ToUpperInvariant(),
                Style = (Style)FindResource("ShelfTitle"),
                Margin = new Thickness(0, 6, 0, 10),
            });

            if (togo.Count == 0)
            {
                HomeStack.Children.Add(new TextBlock
                {
                    Text = I18n.T("nothingToContinue"),
                    Style = (Style)FindResource("Faint"),
                    FontSize = 12.5,
                    Margin = new Thickness(0, 0, 0, 6),
                });
            }
            else
            {
                // Een rij die naar links en naar rechts schuift, net als de andere
                // planken. Zie `AddShelf`.
                var tiles = new StackPanel { Orientation = Orientation.Horizontal };
                foreach (var book in togo) tiles.Children.Add(BookTile(book, showWhere: true, resumable: true));
                HomeStack.Children.Add(new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = tiles,
                    Margin = new Thickness(0, 0, 0, 8),
                });
            }

            AddShelf(I18n.T("addedRecently"), home.Recent, showWhere: false);
        }
        foreach (var why in complaints) AddComplaint(why);
    }

    /// Alleen de linkerkolom vullen: genres, en de twee lijsten die erbij horen.
    ///
    /// Eigenlijk is dit `LoadGenres` zonder het deel dat de rechterkolom leegmaakt
    /// en zonder het genre dat je had gekozen opnieuw te openen â€” want dat is
    /// goed voor als je bladert en verkeerd als je net thuiskomt.
    private async Task LoadLeftOnly()
    {
        try
        {
            var genres = await FetchLeft();
            LeftStack.Children.Clear();
            FillLeft(genres);
        }
        catch (Fault f)
        {
            LeftStack.Children.Clear();
            LeftStack.Children.Add(new TextBlock
            {
                Text = f.Text, Style = (Style)FindResource("Dim"),
                TextWrapping = TextWrapping.Wrap,
            });
        }
    }

    /// De drie lijsten die de linkerkolom voedt, in de velden gezet.
    ///
    /// Alleen het genre moet slagen: zonder genres is de kolom leeg en heeft de
    /// thuispagina geen uitweg meer. De andere twee zijn extra's, en een extra
    /// dat niet komt is geen reden om het genre te vergeten.
    private async Task<List<Genre>> FetchLeft()
    {
        var genres = await _collection.Genres(_stop.Token);
        try { _favourites = await _collection.Favourites(_stop.Token); }
        catch (Fault) { _favourites = null; }
        if (_favourites is not null)
            _hearts = _favourites.Select(f => f.Id).ToHashSet();
        try { _listened = await _collection.Listened(_stop.Token); }
        catch (Fault) { _listened = new List<Book>(); }
        return genres;
    }

    /// Een regel onderaan de pagina die zegt wat er ontbreekt.
    ///
    /// Bewust onder de planken en niet erboven: de planken zijn wat je wilde
    /// zien, en een klacht die alles naar beneden duwt is een klacht die het
    /// belangrijkste wegdrukt.
    private void AddComplaint(string why)
    {
        HomeStack.Children.Add(new TextBlock
        {
            Text = why,
            Style = (Style)FindResource("Dim"),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 14, 0, 0),
        });
    }

    /// De tellingen van de collectie, in de balk onderaan.
    ///
    /// Niet in de pagina. Ze staan op de site in de voettekst, en dat klopt:
    /// het zijn geen boeken maar de collectie, ze veranderen niet als je van blad
    /// wisselt, en ze horen dus niet tussen de planken te staan. Ze blijven ook
    /// staan als het ophalen mislukt, met de laatste cijfers die je had: een
    /// balk die leeg wordt is een balk die zegt dat je collectie leeg is, en dat
    /// is iets anders dan zeggen dat het even niet gelukt is.
    private void AddStats(Stats? stats)
    {
        if (stats is null) return;
        _stats = stats;
        StatusStack.Children.Clear();

        // Zo staat het in `#status` op de site: het getal dik en in de tekstkleur,
        // het woord vaag, en 20 pixels ertussen. Geen puntjes ertussen â€” de
        // kleur doet het werk, en puntjes zijn een streepje dat de site ook niet
        // heeft.
        void Getal(long n, string wat)
        {
            var pair = new StackPanel { Orientation = Orientation.Horizontal };
            pair.Children.Add(new TextBlock
            {
                Text = I18n.Number(n),
                Style = (Style)FindResource("Text"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            });
            pair.Children.Add(new TextBlock
            {
                Text = wat,
                Style = (Style)FindResource("Faint"),
                FontSize = 12,
                Margin = new Thickness(5, 1, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            pair.Margin = new Thickness(0, 0, 20, 0);
            StatusStack.Children.Add(pair);
        }

        Getal(stats.Books, I18n.T("statBooks"));
        Getal(stats.Files, I18n.T("statFiles"));
        Getal(stats.Done, I18n.T("statDone"));
    }

    private void AddShelf(string title, List<Book> books, bool showWhere, bool resumable = false)
    {
        if (books.Count == 0) return;
        // `margin: 6px 0 10px`, want dat zegt de CSS. Eerst stond hier 18, en dat
        // schoof elke plank 12 pixels verder omlaag dan op de site; met zes
        // planken staat de laatste dan meer dan een halve regel te laag.
        var head = new TextBlock
        {
            // `.1em`, want dat is wat de CSS van `.shelf-title` zegt.
            Text = Spaced(title, 0.1),
            Style = (Style)FindResource("ShelfTitle"),
            Margin = new Thickness(0, 6, 0, 10),
        };
        AutomationProperties.SetName(head, title);
        HomeStack.Children.Add(head);

        // Een rij die naar links en naar rechts schuift, en niet een blok dat naar
        // beneden groeit. Zo staat het op de site, en het is niet alleen mooier:
        // een thuispagina met zes planken die allemaal naar beneden lopen is een
        // pagina waar je doorheen moet scrollen om te zien wat er is, terwijl een
        // rij per plank meteen te zien is en alleen zijwaarts hoeft.
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var book in books) row.Children.Add(BookTile(book, showWhere, resumable));
        // `margin-bottom: 6px` van `.shelf`.
        HomeStack.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = row,
            Margin = new Thickness(0, 0, 0, 6),
        });
    }

    /// EÃ©n boek als tegel: alleen de omslag, de titel en de naam, en de balk
    /// waarmee je ziet hoe ver je was.
    ///
    /// Geen rand en geen vlak eromheen. Dat is een keuze van de site en hij is
    /// de goede: een plaatje dat in een doos zit, is een plaatje dat zegt
    /// "klik hier", en op een plank met veertig boeken is veertig keer zeggen
    /// "klik hier" hetzelfde als niets zeggen. De doos hoort bij de lijst met
    /// boeken die je nog moet kiezen, niet bij de plank.
    private Button BookTile(Book book, bool showWhere, bool resumable = false)
    {
        var cover = new Image
        {
            Width = 104,
            Height = 156,
            Stretch = Stretch.UniformToFill,
            SnapsToDevicePixels = true,
        };
        // Afgeronde hoeken zonder een foto: een hoekje van de omslag wordt
        // afgeknipt met een Clip. Dat is goedkoper dan de hele omslag in een
        // sjabloon zetten en ernaar rekenen.
        cover.Clip = new RectangleGeometry(new Rect(0, 0, 104, 156), 8, 8);

        var stack = new StackPanel { Width = 104 };
        stack.Children.Add(cover);

        if (showWhere && book.HasStarted && !book.IsFinished)
        {
            stack.Children.Add(new ProgressBar
            {
                Value = Math.Clamp(book.Percent ?? 0, 0, 100),
                Maximum = 100,
                Margin = new Thickness(0, 6, 0, 3),
            });
        }

        stack.Children.Add(new TextBlock
        {
            Text = book.Title,
            Style = (Style)FindResource("Text"),
            FontSize = 12.5,
            Margin = new Thickness(0, 7, 0, 0),
            MaxHeight = 33,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        // De serie, als het boek er in staat, in het cyaan. Zo staat het op de
        // site, en het is de enige plek waar de serie zichtbaar is zonder dat
        // je het boek opent â€” en juist bij "Verder luisteren" weet je vaak alleen
        // nog de titel.
        if (book.HasSeries)
        {
            stack.Children.Add(new TextBlock
            {
                Text = book.Series,
                Style = (Style)FindResource("Faint"),
                Foreground = (Brush)FindResource("Accent2"),
                FontSize = 11.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }
        else if (book.Author.Length > 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = book.Author,
                Style = (Style)FindResource("Faint"),
                FontSize = 11.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }

        var button = new Button
        {
            Content = stack,
            Style = (Style)FindResource("Plain"),
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 12, 14),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        // De parameter heet hier `e` en niet `_`, en dat is geen smaak maar een
        // onderscheid: met een `_` als naam is de `_ = OpenBook(...)` in de
        // body geen weggegooid resultaat maar een toewijzing aan die parameter,
        // en de aanroep van de asynchrone methode verdwijnt dan stil in een
        // `RoutedEventArgs` die nergens gebruikt wordt. De klik doet dan niets.
        //
        // En in "Verder luisteren" speelt klikken het boek af, in plaats van het
        // te openen. Zo staat het op de site: `t.dataset.resume === '1' ?
        // playBook(...) : openInLibrary(...)`, en die `1` krijgt een boek dat je
        // al begonnen hebt. Dat is het verschil tussen een plank waar je kiest
        // wat je gaat luisteren en een plank waar je verdergaat met wat je al
        // begonnen had. Er staat nergens een tweede klik meer tussen.
        var id = book.Id;
        button.Click += (s, e) => _ = resumable && book.HasStarted
            ? ResumeBook(id, book)
            : OpenBook(id);
        AutomationProperties.SetName(button, resumable && book.HasStarted
            ? I18n.T("resumeThis", ("t", book.Title))
            : book.Title);

        _covers.Put(cover, book.Id, book.Cover, book.CoverV, _stop.Token);
        return button;
    }

    /// EÃ©n boek als kaart in de lijst: omslag, titel, regels, en een knop om
    /// het meteen te spelen.
    ///
    /// Hier hoort de doos wÃ©l om, want dit is de lijst waar je kiest, en de
    /// regels eronder zijn wat je leest om te kiezen. De knop rechts staat er
    /// om te hervatten zonder eerst het boek open te doen, en dat is het hele
    /// punt van hervatten: wie er gisteren mee begonnen is wil het vanavond
    /// verder luisteren en niet eerst een pagina lezen.
    private Button BookCard(Book book, bool showWhere)
    {
        var cover = new Image
        {
            Width = 96,
            Height = 144,
            Stretch = Stretch.UniformToFill,
            SnapsToDevicePixels = true,
        };
        cover.Clip = new RectangleGeometry(new Rect(0, 0, 96, 144), 6, 6);
        _covers.Put(cover, book.Id, book.Cover, book.CoverV, _stop.Token);

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = book.Title,
            Style = (Style)FindResource("Text"),
            FontSize = 15,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var sub = new List<string>();
        if (book.Author.Length > 0) sub.Add(book.Author);
        if (book.HasSeries) sub.Add(book.Series!);
        if (sub.Count > 0)
        {
            var line = new TextBlock
            {
                Text = string.Join(" Â· ", sub),
                Style = (Style)FindResource("Dim"),
                FontSize = 12.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            if (book.HasSeries) line.Foreground = (Brush)FindResource("Accent2");
            stack.Children.Add(line);
        }

        if (showWhere && book.HasStarted && !book.IsFinished)
        {
            stack.Children.Add(new TextBlock
            {
                Text = I18n.T("partNo", ("n", (book.TrackIdx ?? 0) + 1)),
                Style = (Style)FindResource("Faint"),
                FontSize = 11.5,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }

        if (!string.IsNullOrWhiteSpace(book.Description))
        {
            stack.Children.Add(new TextBlock
            {
                Text = book.Description,
                Style = (Style)FindResource("Dim"),
                FontSize = 12.5,
                Margin = new Thickness(0, 6, 0, 0),
                MaxHeight = 54,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }

        var play = new Button
        {
            Content = book.HasStarted && !book.IsFinished ? I18n.T("resumeShort") : I18n.T("playShort"),
            Style = (Style)FindResource("Primary"),
            Padding = new Thickness(14, 7, 14, 7),
            VerticalAlignment = VerticalAlignment.Top,
        };
        var pid = book.Id;
        play.Click += (s, e) => _ = ResumeBook(pid, book);
        AutomationProperties.SetName(play, book.HasStarted && !book.IsFinished
            ? I18n.T("resumeThis", ("t", book.Title))
            : I18n.T("playThis", ("t", book.Title)));
        // Een id dat niet van de taal of de volgorde afhangt; zie `BoekAfspelen`
        // in MainWindow.Book.cs.
        AutomationProperties.SetAutomationId(play, "KaartAfspelen");

        var inside = new Grid();
        inside.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inside.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inside.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inside.Children.Add(cover);
        Grid.SetColumn(stack, 1);
        inside.Children.Add(stack);
        Grid.SetColumn(play, 2);
        inside.Children.Add(play);
        inside.Margin = new Thickness(0, 0, 12, 12);

        var card = new Border
        {
            Background = (Brush)FindResource("Panel"),
            BorderBrush = (Brush)FindResource("Line"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 12, 0),
            Cursor = Cursors.Hand,
            Child = inside,
        };
        var button = new Button
        {
            Content = card,
            Style = (Style)FindResource("Plain"),
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        var id = book.Id;
        button.Click += (s, e) => _ = OpenBook(id);
        card.MouseEnter += (s, e) => card.Background = (Brush)FindResource("Panel2");
        card.MouseLeave += (s, e) => card.Background = (Brush)FindResource("Panel");
        return button;
    }

    /// Vanuit de lijst hervatten, zonder eerst het boek te openen.
    ///
    /// De lijst weet in welk deel en hoe ver iemand was, maar niet wat er in dat
    /// deel staat â€” en de speler wil een heel boek. Dus Ã©Ã©n verzoek voor het
    /// boek, en dan verder. Faalt dat, dan staat er wat er mis was, want een
    /// knop die doet alsof hij werkte is erger dan geen knop.
    private async Task ResumeBook(long id, Book listed)
    {
        try
        {
            var detail = await _collection.Book(id, _stop.Token);
            StartPlaying(detail, listed.TrackIdx ?? 0, listed.Position ?? 0);
        }
        catch (Fault f)
        {
            Say(f.Text);
        }
    }

    private async Task GoShelves()
    {
        Show(Where_.Shelves);
        _searching = false;
        await LoadGenres();
    }

    private void OnBrand(object sender, RoutedEventArgs e) => _ = GoHome();

    // --- de drie kolommen ---------------------------------------------------

    private async Task LoadGenres()
    {
        LeftStack.Children.Clear();
        RightWrap.Children.Clear();
        LeftStack.Children.Add(new TextBlock
        {
            Text = I18n.T("loading"), Style = (Style)FindResource("Dim"),
        });

        try
        {
            var genres = await FetchLeft();

            LeftStack.Children.Clear();
            FillLeft(genres);

            if (_genre is not null) await SelectGenre(_genre);
            else PaneHeadTo();
        }
        catch (Fault f)
        {
            LeftStack.Children.Clear();
            LeftStack.Children.Add(new TextBlock
            {
                Text = f.Text, Style = (Style)FindResource("Text"),
            });
        }
    }

    /// De linkerkolom: genres, en de twee lijsten die eronder horen.
    ///
    /// EÃ©n stuk, twee plekken die het gebruiken. Het staat op de thuispagina en
    /// tijdens het bladeren, want de genres zijn de enige manier om van hier naar
    /// daar te komen; twee versies ervan zouden er hetzelfde uit zien tot het
    /// ene om de een of andere reden achterloopt.
    private void FillLeft(List<Genre> genres)
    {
        // De hele rail, en dus ook leegmaken eerst. Dat is geen gewoonte maar de
        // afspraak van deze methode: hij wordt aangeroepen vanuit het kiezen van
        // een genre, en die wist niet dat de lijst nog vol stond â€” waardoor de
        // genres, de series en de favorieten er twee keer in stonden, met de
        // auteurs ertussenin.
        LeftStack.Children.Clear();
        SetColTitle(LeftHead, I18n.T("genres"));
        foreach (var g in genres)
        {
            var name = g.Name;
            var open = _store.OpenGenres.Contains(name);

            // Eén rijvorm voor alle genres, met of zonder series, en met de
            // maat van het pijltje uit de CSS: `#genres .twist { width: 11px }`.
            //
            // Het lijkt verleidelijk om dat gat altijd open te houden, zodat elke
            // naam op dezelfde plek begint, maar dan begint de naam van een genre
            // mét pijltje op 20 en zonder op 9, en dat is een moeilijkere
            // afwijking dan de scheve rand die je ermee wegnam. De site heeft de
            // ongelijke rand ook, en daar is hij de regel: een genre met series
            // heeft iets in te klappen en dat moet je eraf zien.
            var hasSeries = g.Series.Count > 0;
            var genreName = name;

            UIElement? lead = null;
            if (hasSeries)
            {
                // Het pijltje klapt de series uit en uit, en kiest het genre niet.
                // Zo staat het op de site, en het onderscheid is de moeite waard:
                // wie de series van Science Fiction wil zien hoeft Science Fiction
                // niet te kiezen, en andersom kies je Science Fiction en zie je
                // de series meteen.
                //
                // Het is een apart knopje in de marge van de rij, en daarom zit
                // het in dezelfde knop: een tweede knop naast de eerste met een
                // eigen rand en eigen oplichting, en die leest als twee dingen
                // waar het er één is.
                var arrow = new Button
                {
                    Content = new TextBlock
                    {
                        // CSS zegt hier 10px voor het pijltje en 13px voor de
                        // naam, dus 3px kleiner, en dat is de enige manier om een
                        // driehoek in een vak te laten zitten zonder dat hij
                        // omhoog springt.
                        Text = open ? "▾" : "▸",
                        Style = (Style)FindResource("Dim"),
                        FontSize = 10,
                        TextAlignment = TextAlignment.Center,
                    },
                    Style = (Style)FindResource("Plain"),
                    // Een pijltje van 11 pixels is een raakvlak van 11 bij 33.
                    // De CSS vergroot het daarom ook (`padding: 4px 6px`), en dat
                    // haalt hij hier uit de regel heen in plaats van eruit te
                    // steken, zodat de rij niet 8 pixels breder wordt.
                    Padding = new Thickness(6, 4, 6, 4),
                    Margin = new Thickness(-6, -9, -6, -9),
                    Cursor = Cursors.Hand,
                };
                arrow.Click += (s, e) => _ = ToggleSeriesOf(genreName);
                AutomationProperties.SetName(arrow, I18n.T("foldSeries", ("name", name)));
                AutomationProperties.SetAutomationId(arrow, "GenreKlappen");
                lead = arrow;
            }

            var pick = Row(name, name == _genre, I18n.Number(g.Books),
                lead: lead, gutter: hasSeries ? 11 : 0);
            pick.Click += (s, e) => _ = SelectGenre(name);
            AutomationProperties.SetAutomationId(pick, "GenreKiezen");
            LeftStack.Children.Add(pick);

            if (!hasSeries || !open) continue;
            foreach (var s in g.Series)
            {
                var seriesName = s.Name;
                var forGenre = genreName;
                var srow = Row(seriesName, seriesName == _series, I18n.Number(s.Books));
                // Onder het genre, en met zijn eigen inspringing: de CSS geeft
                // `li.series-in-genre { padding: 6px 12px 6px 30px }`, en dat
                // is 10 pixels verder dan de 20 van een gewone regel.
                srow.Padding = new Thickness(30, 9, 12, 9);
                srow.Foreground = (Brush)FindResource("InkDim");
                srow.Click += (a, b) => _ = SelectSeries(forGenre, seriesName);
                LeftStack.Children.Add(srow);
            }
        }

        // De auteurs staan in de eigen kolom naast deze, en niet hier. Zie de
        // opmerking bij het raster in de XAML.

        // De boeken die je half hebt. Hier, als een lijst, en niet alleen als
        // tegels op de thuispagina.
        //
        // De site heeft er ook een plek voor, links: de lijst met wat je
        // beluisterd hebt. Dat is er niet voor de vorm, maar omdat het de enige
        // plek is waar je ziet wat je bent begonnen zonder eerst terug te
        // hoeven naar de thuispagina. En je staat er vaak genoeg: elke keer dat
        // je een genre kiest of een auteur aanklikt, sta je daar al.
        if (_continue.Count > 0)
        {
            LeftStack.Children.Add(Header(I18n.T("continueListening"), 22));
            foreach (var b in _continue)
            {
                var row = Row(b.Title, false, I18n.T("partNo", ("n", (b.TrackIdx ?? 0) + 1)));
                var id = b.Id;
                row.Click += (s, e) => _ = OpenBook(id);
                LeftStack.Children.Add(row);
            }
        }

        // Twee secties die er alleen zijn als de server ze kent. Een
        // "Favorieten" die nooit iets kan bevatten is iets dat niemand wil
        // zien, en een lege "Beluisterd" zegt niets zolang er nog niets af is.
        if (_favourites is not null)
        {
            LeftStack.Children.Add(Header(I18n.T("favourites"), 22));
            if (_favourites.Count == 0)
            {
                LeftStack.Children.Add(new TextBlock
                {
                    Text = I18n.T("noHearts"), Style = (Style)FindResource("Faint"),
                    FontSize = 12, Margin = new Thickness(8, 0, 8, 4), TextWrapping = TextWrapping.Wrap,
                });
            }
            foreach (var f in _favourites)
            {
                var row = Row(f.Title, false);
                var id = f.Id;
                row.Click += (s, e) => _ = OpenBook(id);
                LeftStack.Children.Add(row);
            }
        }

        if (_listened.Count > 0)
        {
            LeftStack.Children.Add(Header(I18n.T("listened"), 22));
            foreach (var f in _listened)
            {
                var row = Row(f.Title, false);
                var id = f.Id;
                row.Click += (s, e) => _ = OpenBook(id);
                LeftStack.Children.Add(row);
            }
        }
    }

    /// De series van een genre in- of uitklappen, en dat onthouden.
    ///
    /// Onthouden, want een genre met vijf series wil je niet bij elke start opnieuw
    /// uitklappen. `openGenres` stond al in de instellingen en werd nergens
    /// gelezen: de app wist dat er iets mee moest en deed het niet.
    private async Task ToggleSeriesOf(string genre)
    {
        var open = new List<string>(_store.OpenGenres);
        if (!open.Remove(genre)) open.Add(genre);
        _store.SetOpenGenres(open);
        await LoadLeftOnly();
    }

    private async Task SelectGenre(string genre)
    {
        _genre = genre;
        _author = null;
        _series = null;
        _shown = new();
        Show(Where_.Shelves);
        PaneHeadTo();
        RightWrap.Children.Clear();
        RightWrap.Children.Add(new TextBlock
        {
            Text = I18n.T("loading"), Style = (Style)FindResource("Dim"),
        });

        try
        {
            var authors = await _collection.Authors(genre, _stop.Token);
            var genres = await _collection.Genres(_stop.Token);
            var here = genres.FirstOrDefault(g => g.Name == genre);

            // De auteurs in de eigen kolom, met de serie-aantallen eronder zoals
            // de site dat doet. De kolom blijft leeg tot je een genre kiest, en
            // dat is de reden dat het een eigen kolom is.
            _authors = authors;
            FillLeft(genres);

            MiddleStack.Children.Clear();
            SetColTitle(MiddleHead, I18n.T("authors"));
            if (authors.Count == 0)
            {
                MiddleStack.Children.Add(new TextBlock
                {
                    Text = I18n.T("noAuthors"), Style = (Style)FindResource("Faint"),
                    FontSize = 12, Margin = new Thickness(12, 0, 0, 4),
                });
            }
            foreach (var a in authors)
            {
                var name = a.Name;
                var row = Row(name, name == _author, I18n.Number(a.Books));
                row.Click += (s, e) => _ = SelectAuthor(name);
                MiddleStack.Children.Add(row);
            }

            // Er eerst weer leeg, want hier staat nog de wacht. Zonder die
            // leegmaak stonden ze naast elkaar — RightWrap is een WrapPanel, en
            // die zet twee blokken die beide passen op één regel. Zo las het als
            // "One moment… Pick an author.", en het leek alsof de tweede al
            // klaar was terwijl de eerste nog bezig was.
            RightWrap.Children.Clear();
            RightWrap.Children.Add(new TextBlock
            {
                Text = I18n.T("pickAuthor"),
                Style = (Style)FindResource("Dim"),
                Margin = new Thickness(4, 8, 0, 0),
            });
        }
        catch (Fault f)
        {
            RightWrap.Children.Clear();
            RightWrap.Children.Add(new TextBlock { Text = f.Text, Style = (Style)FindResource("Text") });
        }
    }

    private async Task SelectAuthor(string author)
    {
        if (_genre is null) return;
        _author = author;
        _series = null;
        await LoadBooks(author, async () => await _collection.BooksByAuthor(_genre!, author, _stop.Token));
    }

    /// De boeken van een reeks.
    ///
    /// De genre gaat mee, want de server vraagt ernaar. Je komt hier binnen
    /// vanaf de lijst met series in de linkerkolom, en daar is geen genre
    /// gekozen â€” dus die staat bij de reeks zelf.
    private async Task SelectSeries(string genre, string series)
    {
        _genre = genre;
        _series = series;
        _author = null;
        await LoadBooks(series, async () => await _collection.BooksBySeries(genre, series, _stop.Token));
    }

    private async Task LoadBooks(string title, Func<Task<BooksPage>> fetch)
    {
        Show(Where_.Shelves);
        PaneHeadTo();
        RightWrap.Children.Clear();
        RightWrap.Children.Add(new TextBlock
        {
            Text = I18n.T("loading"), Style = (Style)FindResource("Dim"), Margin = new Thickness(4, 8, 0, 0),
        });
        try
        {
            var page = await fetch();
            _shown = page.Books;
            RightWrap.Children.Clear();
            if (_shown.Count == 0)
            {
                RightWrap.Children.Add(new TextBlock
                {
                    Text = I18n.T("noBooks"), Style = (Style)FindResource("Dim"), Margin = new Thickness(4, 8, 0, 0),
                });
                return;
            }
            foreach (var b in _shown) RightWrap.Children.Add(BookCard(b, showWhere: true));
            AddSeriesNotes(page.Series);
        }
        catch (Fault f)
        {
            RightWrap.Children.Clear();
            RightWrap.Children.Add(new TextBlock
            {
                Text = f.Text, Style = (Style)FindResource("Text"), Margin = new Thickness(4, 8, 0, 0),
            });
        }
    }

    /// Wat er aan een serie ontbreekt. De server schrijft de zin; hier staat hij
    /// alleen neer. Alleen als er iets te zeggen valt: een serie van Ã©Ã©n boek is
    /// geen serie met een gat erin.
    private void AddSeriesNotes(List<SeriesInfo> series)
    {
        if (series.Count == 0) return;
        RightWrap.Children.Add(new Border
        {
            Width = 280,
            Margin = new Thickness(0, 6, 14, 16),
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(10),
            Background = (Brush)FindResource("Panel"),
            BorderBrush = (Brush)FindResource("Line"),
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = I18n.T("series"), Style = (Style)FindResource("Section") },
                    new TextBlock
                    {
                        Text = string.Join("\n", series.Where(s => s.Says.Length > 0).Select(s => $"{s.Name}: {s.Says}")),
                        Style = (Style)FindResource("Dim"),
                        FontSize = 12,
                        Margin = new Thickness(0, 4, 0, 0),
                    },
                },
            },
        });
    }

    // --- zoeken -------------------------------------------------------------

    private async void OnSearchText(object sender, TextChangedEventArgs e)
    {
        var q = Search.Text.Trim();
        SearchHint.Visibility = q.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (q.Length < 2)
        {
            if (_searching) { _searching = false; _searchFor = ""; }
            return;
        }

        // Even wachten voor er echt gezocht wordt. Iedere letter zou anders een
        // verzoek zijn, en de server zou een stuk collectie doorzoeken voor elk
        // woord dat je typt.
        _searchFor = q;
        _searching = true;
        await Task.Delay(350);
        if (!_searching || _searchFor != q) return;

        Show(Where_.Shelves);
        PaneHeadTo();
        LeftStack.Children.Clear();
        RightWrap.Children.Clear();
        SetColTitle(LeftHead, I18n.T("searching"));

        try
        {
            var found = await _collection.Search(q, _stop.Token);
            if (_searchFor != q) return; // er is inmiddels iets anders getypt
            _shown = found;
            RightWrap.Children.Clear();
            if (found.Count == 0)
            {
                RightWrap.Children.Add(new TextBlock
                {
                    Text = I18n.T("nothingMatches", ("q", q)),
                    Style = (Style)FindResource("Dim"),
                    Margin = new Thickness(4, 8, 0, 0),
                });
                return;
            }
            foreach (var b in found) RightWrap.Children.Add(BookCard(b, showWhere: true));
        }
        catch (Fault f)
        {
            RightWrap.Children.Clear();
            RightWrap.Children.Add(new TextBlock
            {
                Text = f.Text, Style = (Style)FindResource("Text"), Margin = new Thickness(4, 8, 0, 0),
            });
        }
    }

    private async void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Search.Text = "";
        _searching = false;
        _searchFor = "";
        // Naar huis, en niet naar de planken. `GoShelves` stond hier, en dat is
        // een plek die alleen bestaat als je al aan het bladeren bent: vanuit een
        // zoekopdracht stuurde de escape je dus naar een pagina zonder planken.
        // Ontsnappen hoort je altijd terug te brengen waar je begon.
        await GoHome();
    }

    // --- kleine stukjes -----------------------------------------------------

    /// Een aantal seconden als `12:34`, of `1:02:03` vanaf een uur.
    ///
    /// De uren staan er pas vanaf een uur bij, omdat "0:12:34" eruitziet als een
    /// fout en niet als een tijd.
    static string Clock(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
        var total = (int)Math.Round(seconds);
        var h = total / 3600;
        var m = (total % 3600) / 60;
        var s = total % 60;
        return h > 0
            ? $"{h}:{m:00}:{s:00}"
            : $"{m}:{s:00}";
    }

    /// Wit op het paarse vlak van een gekozen rij.
    ///
    /// De CSS heeft er één kleur voor, op drie plekken: het woord, het aantal en
    /// het pijltje van een actieve regel (`li.active`, `li.active .count`,
    /// `li.active .twist`). Eén keer per keer hier typen is vragen om er later
    /// een te vergeten, dus het staat op één plek.
    static readonly Brush OnAccent = new SolidColorBrush(Color.FromRgb(0xE8, 0xE2, 0xFF));

    /// De letters van een kop uit elkaar zetten, want de site doet dat.
    ///
    /// `letter-spacing: .12em` op `.col-title` en `.1em` op `.shelf-title`, en dat
    /// is geen detail: het verschil tussen "GENRES" en "G E N R E S" is een
    /// klassiek knopje-label, en zonder de ruimte ziet het eruit alsof er iemand
    /// heeft geschreeuwd. WPF kent `letter-spacing` niet, dus het enige dat werkt
    /// is er zelf spaties tussen zetten. Dunne: U+200A is een hair space, en dat
    /// is 0,1 em breed. Eén van die per letter is ongeveer `.1em`, twee per letter
    /// ongeveer `.12em`.
    ///
    /// Dat het een echte spatie in de tekst is, heeft één nadeel: een schermlezer
    /// en een zoekactie zien de spaties ook. Daarom zet elke plek die dit
    /// gebruikt de schone tekst apart in `AutomationProperties.SetName`, en is er
    /// `Spaced` altijd gepaard met zo'n naam. De naam is wat je hoort, de tekst is
    /// wat je ziet, en dat verschil is hier de bedoeling.
    static string Spaced(string text, double em = 0.1)
    {
        var hair = em >= 0.12 ? "\u200A\u200A" : "\u200A";
        return string.Join(hair, text.ToUpperInvariant());
    }

    /// De tekst van een kop in de bovenste rij zetten: met de letterruimte erin
    /// voor het oog, en zonder voor het oor.
    ///
    /// De drie vaste koppen staan in de XAML en worden vanuit vier plekken
    /// omgezet — genres, auteurs, zoeken, leeg. Overal dezelfde regel gebruiken,
    /// want de ene plek waar de hairspaces ontbreken is meteen de plek die je
    /// ziet.
    static void SetColTitle(TextBlock block, string text)
    {
        block.Text = Spaced(text, 0.12);
        AutomationProperties.SetName(block, text);
    }

    /// De kop boven een kolom of een lijst: klein, hoofdletters, vaag.
    ///
    /// Zo staat hij op de site, en de reden daarvan is de moeite waard om over te
    /// nemen: het is een naam van een lijst, geen opschrift. Wie de app opent wil
    /// boeken zien, en niet lezen dat er een lijst is.
    private static TextBlock Header(string text, double top = 6)
    {
        // `.12em`, want dat is wat de CSS van `.col-title` zegt.
        var block = new TextBlock
        {
            Text = Spaced(text, 0.12),
            Style = (Style)Application.Current.FindResource("ColTitle"),
            Margin = new Thickness(16, top, 16, 8),
        };
        // De hairspaces tussen de letters horen bij wat je ziet en niet bij wat je
        // hoort, dus de naam is de schone tekst.
        AutomationProperties.SetName(block, text);
        return block;
    }

    /// Een regel in een kolom: de naam links, het aantal rechts, en optioneel
    /// een pijltje links ervan.
    ///
    /// Die drie stukken zijn een rij, en de rij is een klikvlak: `#genres li` is
    /// een `display: flex` met het pijltje, de naam en het aantal ertussen. Zet
    /// het pijltje buiten die rij, dan zit er een tweede knop naast de eerste met
    /// een eigen rand, en het leest als twee dingen waar het er een is, terwijl
    /// het er een is: dit genre.
    ///
    /// Het gat van het pijltje is 11 pixels, want dat zegt de CSS
    /// (`#genres .twist { width: 11px }`). Daardoor begint de naam van een genre
    /// met series 11 pixels verder naar rechts dan de naam van een genre zonder,
    /// en die ongelijke rand is de bedoeling: het genre heeft iets in te klappen
    /// en dat moet je eraf zien. Dus niet weguniformeren.
    ///
    /// Het aantal rechts, en niet vastgeplakt aan de naam: met het aantal
    /// vastgeplakt moet je de breedte van het getal meetellen om te zien waar de
    /// volgende regel begint, en met het aantal rechts staan ze allemaal op
    /// dezelfde plek.
    ///
    /// De gekozen rij is een vlak paars met wit erop, en niet alleen een andere
    /// kleur voor het woord: het gaat erom dat je in een lijst van honderden
    /// ziet waar je bent.
    ///
    /// Het aantal rechts, en niet vastgeplakt aan de naam: met het aantal vastgeplakt
    /// moet je de breedte van het getal meetellen om te zien waar de volgende regel
    /// begint, en met het aantal rechts staan ze allemaal op dezelfde plek.
    ///
    /// De gekozen rij is een vlak paars met wit erop, en niet alleen een andere
    /// kleur voor het woord: het gaat erom dat je in een lijst van honderden
    /// ziet waar je bent.
    private Button Row(string text, bool active, string? count = null,
        UIElement? lead = null, double gutter = 0)
    {
        var line = new Grid();
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gutter) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (count is not null) line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (lead is not null) line.Children.Add(lead);

        var name = new TextBlock
        {
            Text = text,
            Style = (Style)Application.Current.FindResource("Text"),
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(name, 1);
        line.Children.Add(name);

        if (count is not null)
        {
            var number = new TextBlock
            {
                Text = count,
                Style = (Style)Application.Current.FindResource("Faint"),
                FontSize = 12,
                Margin = new Thickness(8, 1, 0, 0),
            };
            if (active) number.Foreground = OnAccent;
            Grid.SetColumn(number, 2);
            line.Children.Add(number);
        }

        var button = new Button
        {
            Content = line,
            Style = (Style)Application.Current.FindResource("Plain"),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0, 9, 12, 9),
            Margin = new Thickness(8, 0, 8, 0),
            Tag = active,
        };
        if (active)
        {
            button.Background = (Brush)Application.Current.FindResource("Accent");
            button.Foreground = Brushes.White;
            name.Foreground = Brushes.White;
            // Het pijltje ook, want een grijs pijltje op een paars vlak is een
            // pijltje dat je niet ziet: `#genres li.active .twist` zet hem op
            // #e8e2ff, een tintje lichter dan het vlak.
            //
            // Soms is het pijltje een losse TextBlock en soms een knopje met een
            // driehoek erin, want in WPF kun je niet klikken op wat niet klikbaar
            // is. Beide wegen uit, want anders vergeet het er een keer bij.
            if (lead is TextBlock flat)
                flat.Foreground = OnAccent;
            else if (lead is Button arrow && arrow.Content is TextBlock inArrow)
                inArrow.Foreground = OnAccent;
        }

        // De rij is een raster met twee stukken tekst, en zo'n knop heeft
        // standaard geen naam: een schermlezer zegt dan "knop" en verder niets.
        // De naam staat er, dus zet hem er ook neer.
        AutomationProperties.SetName(button,
            count is null ? text : $"{text}, {count}");

        return button;
    }

    private void Say(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        _toastNumber++;
        ToastText.Text = text;
        Toast.Visibility = Visibility.Visible;
        var mine = _toastNumber;
        // Zeven seconden, en niet de 2,6 van de site. De site zet er een zin in
        // van één regel ("Aantal 3, boeken 12 klaar"), en die heb je gelezen voor
        // hij weg mag. Hier staat soms een klacht van twee regels die je moet
        // kunnen overtypen om er iets aan te doen, en een melding die weg gaat
        // voordat je hem hebt gelezen is net als geen melding.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            // Alleen weghalen als nog niemand anders iets gezegd heeft. Anders
            // verdwijnt een nieuw bericht meteen omdat het vorige afliep.
            if (mine == _toastNumber) Toast.Visibility = Visibility.Collapsed;
        };
        timer.Start();
    }

    private void OnStoreChanged(object? sender, EventArgs e)
    {
        if (_store.Warning is { } warning && warning != _saveWarning)
        {
            _saveWarning = warning;
            Dispatcher.Invoke(() => Say(warning));
        }
    }

    private void Translate()
    {
        BrandName.Text = I18n.T("appName");
        Brand.ToolTip = I18n.T("start");
        SearchHint.Text = I18n.T("search");
        SignOut.Content = I18n.T("signOut");
        SignOut.ToolTip = I18n.T("signOut");
        SignOut.Visibility = string.IsNullOrEmpty(_collection.User) ? Visibility.Collapsed : Visibility.Visible;
        GateTitle.Text = I18n.T("gateTitle");
        GateHint.Text = I18n.T("gateHint");
        GateWhere.Text = I18n.T("address");
        GateName.ToolTip = I18n.T("name");
        GatePass.ToolTip = I18n.T("password");
        GateGo.Content = I18n.T("signIn");
        GateOther.Content = I18n.T("otherAddress");
        GateAddressHint.Text = I18n.T("addressHint");
        GateAddress.ToolTip = I18n.T("address");
        GateSave.Content = I18n.T("save");
        GateBack.Content = I18n.T("backToSignIn");
        Title = I18n.T("appName");
        BuildBar();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // Het venster onthoudt waar het stond, en waar in het boek je was. Die
        // twee zijn samen precies wat de volgende start nodig heeft om weer op
        // dezelfde plek te beginnen.
        var left = double.IsNaN(Left) ? (double?)null : Left;
        var top = double.IsNaN(Top) ? (double?)null : Top;
        _store.SetWindow(new Store.WindowState
        {
            Width = Width,
            Height = Height,
            X = left,
            Y = top,
        });
        _player.Dispose();
        _collection.Dispose();
        _stop.Cancel();
    }
}
