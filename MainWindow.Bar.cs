using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MyAudiobooks;

public partial class MainWindow
{
    private TextBlock? _barTitle;
    private TextBlock? _barPart;
    private TextBlock? _barTime;
    private TextBlock? _barNote;
    private TextBlock? _barLeft;
    private Button? _barPlay;
    private Slider? _barSeek;
    private ProgressBar? _barWork;
    private Button? _barSleep;
    private Button? _barMute;

    /// Zet de spelerbalk opnieuw op.
    ///
    /// Elke aanroep bouwt de balk helemaal opnieuw op. Dat lijkt zwaar voor iets
    /// dat vier keer per seconde verandert, en in een eenvoudiger programma zou
    /// het een fout zijn. Maar de klok roept deze methode niet aan: die roept
    /// alleen `OnPlayerChanged` aan, en die zet alleen de teksten en de
    /// schuifbalk bij. Deze wordt aangeroepen als er iets *verandert*: een ander
    /// boek, een ander deel, het geluid uit. Dat is zelden, en dan is opnieuw
    /// opbouwen het simpelst en het zekerst: er kan geen halve toestand
    /// achterblijven waarin de balk zegt dat hij iets anders speelt dan hij doet.
    private void BuildBar()
    {
        Bar.Children.Clear();
        _barTitle = null;
        _barPart = null;
        _barTime = null;
        _barNote = null;
        _barLeft = null;
        _barPlay = null;
        _barSeek = null;
        _barSleep = null;
        _barWork = null;
        _barMute = null;

        if (!_player.HasBook)
        {
            Bar.Visibility = Visibility.Collapsed;
            return;
        }
        Bar.Visibility = Visibility.Visible;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // --- links: omslagje, titel, deel ------------------------------------
        var left = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        var thumb = new Image
        {
            Width = 44,
            Height = 60,
            Stretch = Stretch.UniformToFill,
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true,
        };
        thumb.Clip = new RectangleGeometry(new Rect(0, 0, 44, 60), 6, 6);
        if (_player.Book is { } shown)
            _covers.Put(thumb, shown.Id, shown.Cover, shown.CoverV, _stop.Token);
        DockPanel.SetDock(thumb, Dock.Left);
        left.Children.Add(thumb);

        var words = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        _barTitle = new TextBlock
        {
            Style = (Style)FindResource("Text"),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _barPart = new TextBlock
        {
            Style = (Style)FindResource("Faint"),
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        words.Children.Add(_barTitle);
        words.Children.Add(_barPart);
        left.Children.Add(words);
        grid.Children.Add(left);

        // --- midden: de knoppen en de tijd ----------------------------------
        var middle = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MinWidth = 460 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };

        // Een id op elke knop, en niet alleen op de grote. Een proefje moet op
        // "terug" kunnen drukken zonder te tellen hoeveel knoppen er links van
        // staan en zonder de vertaling te hoeven kennen; beide zijn gevolgen
        // van een proefje dat een keer op de verkeerde knop drukte en het geluk
        // had dat het groen leek. `hervatten.ps1` drukt hiermee op Terug.
        var prevPart = Glyph("⏮", I18n.T("prevPart"), () => _player.PreviousPart());
        AutomationProperties.SetAutomationId(prevPart, "SpelerVorigDeel");
        buttons.Children.Add(prevPart);

        var skipBack = Glyph("⏪", I18n.T("back15"), () => _player.Skip(-15));
        AutomationProperties.SetAutomationId(skipBack, "SpelerTerug");
        buttons.Children.Add(skipBack);

        _barPlay = Glyph("▶", I18n.T("playOrPause"), () => _player.Toggle());
        _barPlay.FontSize = 20;
        _barPlay.Padding = new Thickness(14, 4, 14, 4);
        AutomationProperties.SetAutomationId(_barPlay, "SpelerAfspelen");
        buttons.Children.Add(_barPlay);

        var skipForward = Glyph("⏩", I18n.T("forward30"), () => _player.Skip(30));
        AutomationProperties.SetAutomationId(skipForward, "SpelerVooruit");
        buttons.Children.Add(skipForward);

        var nextPart = Glyph("⏭", I18n.T("nextPart"), () => _player.NextPart());
        AutomationProperties.SetAutomationId(nextPart, "SpelerVolgendDeel");
        buttons.Children.Add(nextPart);
        middle.Children.Add(buttons);

        var time = new Grid { Margin = new Thickness(20, 8, 20, 0) };
        time.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        time.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        time.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _barLeft = new TextBlock
        {
            Style = (Style)FindResource("Faint"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // Ook de kloppen een id. Een proefje die wil weten waar de speler
        // gebleven is, leest hiermee het ene getal dat daar staat, en niet
        // "het eerste uurteken in de venster" — dat is precies de manier waarop
        // een proefje ooit de titel van een boek als klok ging lezen.
        AutomationProperties.SetAutomationId(_barLeft, "SpelerTijd");
        _barTime = new TextBlock
        {
            Style = (Style)FindResource("Faint"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        AutomationProperties.SetAutomationId(_barTime, "SpelerDuur");
        _barSeek = new Slider
        {
            Minimum = 0,
            Maximum = 1,
            Margin = new Thickness(10, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsMoveToPointEnabled = true,
            ToolTip = I18n.T("positionIn"),
        };
        // Alleen meebewegen als de muis er niet is. Anders springt de schuifbalk
        // terug naar de plek van de speler terwijl je er net naartoe sleepte, en
        // dat is de ergste manier om te proberen ergens heen te springen.
        _barSeek.PreviewMouseLeftButtonDown += (_, _) => _barSeek.CaptureMouse();
        _barSeek.PreviewMouseUp += (_, _) =>
        {
            _barSeek.ReleaseMouseCapture();
            _player.SeekInPart((int)Math.Round(_barSeek.Value));
        };
        Grid.SetColumn(_barLeft, 0);
        Grid.SetColumn(_barSeek, 1);
        Grid.SetColumn(_barTime, 2);
        time.Children.Add(_barLeft);
        time.Children.Add(_barSeek);
        time.Children.Add(_barTime);
        middle.Children.Add(time);

        // De voortgangsregel van het omzetten. Die staat hier, boven de tijd, en
        // niet ergens in een hoek: er is niets te horen, en zonder deze regel
        // is "niets te horen" hetzelfde als "het werkt niet".
        _barWork = new ProgressBar
        {
            Height = 4,
            Margin = new Thickness(20, 8, 20, 0),
            Minimum = 0,
            Maximum = 100,
            Visibility = Visibility.Collapsed,
        };
        middle.Children.Add(_barWork);
        _barNote = new TextBlock
        {
            Style = (Style)FindResource("Faint"),
            // idem: `Accent` is het goud van de omslagen, en dat is een kleur.
            // In `Style` zou het een InvalidCastException zijn, en dan zou er
            // tijdens het omzetten niets te zien zijn behalve een foutvenster.
            Foreground = (Brush)FindResource("Accent"),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(20, 4, 20, 0),
            Visibility = Visibility.Collapsed,
            TextAlignment = TextAlignment.Center,
        };
        middle.Children.Add(_barNote);

        grid.Children.Add(middle);
        Grid.SetColumn(middle, 1);

        // --- rechts: geluid, slaaptimer, wegzetten ---------------------------
        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var mute = Glyph("🔊", I18n.T("sound"), () => _player.Muted = !_player.Muted);
        // Het teken zegt wat er nu is, en niet altijd hetzelfde. `player.js:179`
        // doet dat ook (`audio.muted || !audio.volume ? '🔇' : '🔊'`), en zonder
        // dit staat er tijdens een proefje "🔊" terwijl er niets te horen valt —
        // een teken dat tegen de werkelijkheid in gaat, en het enige teken dat je
        // van de geluidsregeling ziet.
        //
        // En de naam zegt het ook. De site heeft daar alleen de muisaanwijzer
        // ("Mute or unmute") en zegt dus nergens wat de stand is; voor een
        // schermlezer is dat een knop zonder toestand, en voor een proefje een
        // stand die niet na te gaan is. De naam is voor lezen, niet voor
        // kijken, dus hier staat de toestand er wel in.
        AutomationProperties.SetAutomationId(mute, "SpelerDempen");
        _barMute = mute;
        right.Children.Add(mute);
        var volume = new Slider
        {
            Width = 90,
            Minimum = 0,
            Maximum = 1,
            Margin = new Thickness(6, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = I18n.T("volume"),
        };
        volume.Value = _player.Volume;
        volume.ValueChanged += (_, _) =>
        {
            // Niet terugschrijven als het geluid uit staat: dan zou het volume
            // terug naar 0 gaan en bij het aanzetten stil blijven.
            if (!_player.Muted) _player.Volume = volume.Value;
        };
        right.Children.Add(volume);

        _barSleep = new Button
        {
            Content = "⏱",
            Style = (Style)FindResource("Plain"),
            FontSize = 15,
            ToolTip = I18n.T("sleepTimer"),
            ContextMenu = SleepMenu(),
        };
        AutomationProperties.SetName(_barSleep, I18n.T("sleepTimer"));
        right.Children.Add(_barSleep);
        right.Children.Add(Glyph("✕", I18n.T("closePlayer"), () =>
        {
            _player.Stop();
            Bar.Visibility = Visibility.Collapsed;
        }));
        grid.Children.Add(right);
        Grid.SetColumn(right, 2);

        Bar.Children.Add(grid);
        RefreshBar();
    }

    private ContextMenu SleepMenu()
    {
        var menu = new ContextMenu
        {
            Background = (Brush)FindResource("Panel"),
            Foreground = (Brush)FindResource("Ink"),
            FontFamily = (FontFamily)FindResource("Font"),
        };
        foreach (var minutes in new[] { 5, 15, 30, 45, 60, 90 })
        {
            var value = minutes;
            var item = new MenuItem { Header = I18n.T("sleepTimerStop", ("n", value)) };
            item.Click += (_, _) =>
            {
                _player.StartSleepTimer(value);
                Say(I18n.T("sleepTimerSet", ("n", value)));
            };
            menu.Items.Add(item);
        }
        var stop = new MenuItem { Header = I18n.T("sleepTimerStopNow") };
        stop.Click += (_, _) => _player.CancelSleepTimer();
        menu.Items.Add(new Separator());
        menu.Items.Add(stop);
        return menu;
    }

    private static Button Glyph(string text, string tip, Action go)
    {
        var button = new Button
        {
            Content = text,
            Style = (Style)Application.Current.FindResource("Plain"),
            FontSize = 15,
            ToolTip = tip,
        };
        // De knop toont een teken, en een teken is geen naam: zonder dit zegt
        // een schermlezer "knop" zonder er iets bij te zeggen wat hij doet. De
        // uitleg staat er toch al, in de muisaanwijzer — die is alleen voor
        // muisgebruik. Dus dezelfde tekst, maar dan waar hij opgezocht wordt.
        AutomationProperties.SetName(button, tip);
        button.Click += (_, _) => go();
        return button;
    }

    /// Vier keer per seconde vanuit de speler. Alleen de teksten en de
    /// schuifbalk; niets wordt opnieuw opgebouwd.
    private void OnPlayerChanged()
    {
        Dispatcher.Invoke(RefreshBar, DispatcherPriority.Background);
    }

    private void RefreshBar()
    {
        // `BuildBar` zet alle zeven tegelijk op `null` en daarna samen neer, dus
        // als de titel er is, zijn ze er allemaal. Dat maakt de null-controle
        // hieronder overbodig, maar niet zichtbaar voor de lezer — en een
        // waarschuwing die er nog elke bouw staat is een waarschuwing die
        // niemand meer leest.
        if (!_player.HasBook || _player.Book is not { } book) return;
        if (_barTitle is null || _barPart is null || _barTime is null || _barNote is null
            || _barLeft is null || _barPlay is null || _barWork is null) return;

        _barTitle.Text = book.Title;
        _barPart.Text = book.Parts <= 1
            ? book.Tracks.Count > 0 ? book.Tracks[_player.Part].Title : ""
            : I18n.T("partOf",
                ("n", _player.Part + 1),
                ("t", book.Parts),
                ("title", book.Tracks.Count > _player.Part ? book.Tracks[_player.Part].Title : ""));

        var position = _player.Position;
        var length = _player.PartLength;

        // Links de plek, rechts de duur van dit deel, en niets anders. Dat is
        // precies wat de site heeft: `player.js:143-144` zet `#pAt` op de
        // huidige tijd en `#pOf` op de duur van het spoor, meer niet. Er stond
        // hier eerst nog "… van …" bij, met de resterende tijd van het hele
        // boek, en dat staat nergens op de site; het was een eigen regel in een
        // scherm dat andersover een kopie van de site moet zijn.
        //
        // En een streepje zolang de duur onbekend is, want `0:00` is een bewering
        // en een streepje is een waarheid. Zodra de speler het bestand binnen
        // heeft staat de echte lengte er, want `PartLength` neemt die dan van de
        // lezer.
        _barLeft!.Text = Clock(position.TotalSeconds);
        _barTime!.Text = length > TimeSpan.Zero ? Clock(length.TotalSeconds) : "—";

        if (_barSeek is not null && !_barSeek.IsMouseCaptureWithin)
        {
            _barSeek.Maximum = Math.Max(1, length.TotalSeconds);
            _barSeek.Value = Math.Clamp(position.TotalSeconds, 0, _barSeek.Maximum);
            _barSeek.ToolTip = I18n.T("positionIn") + " — " + _barLeft.Text;
        }

        if (_barPlay is not null)
        {
            var what = I18n.T(_player.IsPlaying ? "pauseIt" : "playIt");
            // Alleen als het verandert. Dit draait vier keer per seconde, en een
            // naam die vier keer per seconde hetzelfde is gezet wordt, is een
            // schermlezer die vier keer per seconde dezelfde naam voorleest.
            if (!string.Equals(_barPlay.ToolTip as string, what, StringComparison.Ordinal))
            {
                _barPlay.Content = _player.IsPlaying ? "⏸" : "▶";
                _barPlay.ToolTip = what;
                AutomationProperties.SetName(_barPlay, what);
            }
        }

        // Hetzelfde voor het geluid, en met dezelfde reden: het teken en de naam
        // volgen de stand, en worden alleen gezet als die verandert.
        if (_barMute is not null)
        {
            var stil = _player.Muted || _player.Volume <= 0;
            var wat = I18n.T(stil ? "soundOff" : "soundOn");
            if (!string.Equals(AutomationProperties.GetName(_barMute), wat, StringComparison.Ordinal))
            {
                _barMute.Content = stil ? "🔇" : "🔊";
                AutomationProperties.SetName(_barMute, wat);
            }
        }

        // Omzetten naar mp3: een regel met wat er gebeurt en een balk erboven.
        // Zonder dit staat er "geen geluid" terwijl de server nog bezig is, en dat
        // is precies het misverstand dat de app wil voorkomen.
        var working = _player.Waiting == Waiting.Converting;
        _barWork!.Visibility = working ? Visibility.Visible : Visibility.Collapsed;
        _barNote!.Visibility = working ? Visibility.Visible : Visibility.Collapsed;
        if (working)
        {
            var total2 = _player.ConvertingTotal;
            _barWork.Value = total2 > 0
                ? Math.Clamp(_player.ConvertingDone / total2 * 100, 0, 100)
                : 0;
            _barWork.IsIndeterminate = total2 <= 0;
            _barNote.Text = total2 > 0
                ? I18n.T("converting",
                    ("done", Clock(_player.ConvertingDone).TrimStart('0', 'h', ' ')),
                    ("total", Clock(total2).TrimStart('0', 'h', ' ')))
                : I18n.T("convertingStart");
        }

        var sleep = _player.SleepLeft;
        if (_barSleep is not null)
        {
            _barSleep.Content = sleep is null ? "⏱" : "⏱ " + Clock(sleep.Value.TotalMinutes * 60);
            // `InkFaint`, niet `Faint`: de eerste is het grijs, de tweede is de
            // stijl van een TextBlock. Een stijl als penseel levert een
            // InvalidCastException, en omdat dit vier keer per seconde loopt, is
            // het dan een foutmelding per kwartier in plaats van een klok.
            _barSleep.Foreground = (Brush)FindResource(sleep is null ? "InkFaint" : "Accent");
        }
    }

    /// Het deel is uit. Een volgend deel gaat vanzelf; was dit het laatste, dan is
    /// het boek af.
    private void OnPartEnded()
    {
        Dispatcher.Invoke(async () =>
        {
            var book = _player.Book;
            if (book is null) return;
            if (_player.Part + 1 < book.Parts)
            {
                _player.NextPart();
                return;
            }
            // Het laatste deel is uit. De server zet zijn tik op de plek die zojuist
            // is weggeschreven, en de thuispagina haalt het boek daarom van
            // "verder luisteren" naar "beluisterd". Die twee kunnen niet uit
            // elkaar lopen: ze hebben allebei de server als waarheid.
            await _collection.SavePlace(book.Id, _player.Part,
                book.Tracks.Count > _player.Part ? book.Tracks[_player.Part].Duration : 0, _stop.Token);
            _player.Stop();
            Bar.Visibility = Visibility.Collapsed;
            Say(I18n.T("finished"));
            if (_where == Where_.Book && _open?.Id == book.Id) await OpenBook(book.Id);
            else await GoHome();
        });
    }
}
