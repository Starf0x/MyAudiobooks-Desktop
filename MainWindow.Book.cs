using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MyAudiobooks;

public partial class MainWindow
{
    /// Open één boek over de hele breedte, met de volledige beschrijving erop.
    ///
    /// De beschrijving is hier de reden voor: in een rij boeken is er geen
    /// ruimte voor een synopsis, en een synopsis is precies wat je nodig hebt
    /// om te kiezen of je dit boek wilt. Daarom geen samenvatting en geen
    /// afkapte regel — de hele tekst, en de pagina scrolt.
    private async Task OpenBook(long id)
    {
        Search.Text = "";
        _searching = false;
        _bookCameFrom = _where == Where_.Book ? _bookCameFrom : _where;
        Show(Where_.Book);
        BookGrid.Children.Clear();
        BookGrid.Children.Add(new TextBlock
        {
            Text = I18n.T("loading"),
            Style = (Style)FindResource("Dim"),
        });

        BookDetail detail;
        try
        {
            detail = await _collection.Book(id, _stop.Token);
        }
        catch (Fault f)
        {
            BookGrid.Children.Clear();
            BookGrid.Children.Add(new TextBlock { Text = f.Text, Style = (Style)FindResource("Text") });
            return;
        }

        _open = detail;
        BuildBook(detail);
    }

    private void BuildBook(BookDetail book)
    {
        BookGrid.Children.Clear();
        BookGrid.RowDefinitions.Clear();
        BookGrid.ColumnDefinitions.Clear();
        BookGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        BookGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // --- bovenste rij: terug, en het hart als de server het kent ---------
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
        var back = new Button
        {
            Content = I18n.T("backToShelves"),
            Style = (Style)FindResource("Plain"),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        back.Click += (s, e) => _ = GoBackFromBook();
        AutomationProperties.SetAutomationId(back, "TerugNaarPlanken");
        DockPanel.SetDock(back, Dock.Left);
        top.Children.Add(back);

        if (_favourites is not null)
        {
            var hearted = _hearts.Contains(book.Id);
            var heart = new Button
            {
                Content = hearted ? "♥" : "♡",
                Style = (Style)FindResource("Plain"),
                FontSize = 22,
                // `InkFaint` en niet `Faint`: `Faint` is de stijl van een
                // TextBlock, en een stijl als kleur is een cast die niet lukt.
                Foreground = (Brush)FindResource(hearted ? "Red" : "InkFaint"),
                ToolTip = I18n.T(hearted ? "inYourFavourites" : "addToFavourites"),
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            // Zelfde verhaal als bij de deelknoppen: het teken is geen naam.
            AutomationProperties.SetName(heart, I18n.T(hearted ? "inYourFavourites" : "addToFavourites"));
            heart.Click += async (_, _) => await ToggleHeart(book, heart);
            DockPanel.SetDock(heart, Dock.Right);
            top.Children.Add(heart);
        }
        BookGrid.Children.Add(top);
        Grid.SetRow(top, 0);

        // --- de drie kolommen: omslag links, tekst midden, delen rechts ------
        //
        // Eén rij, drie kolommen. Links de omslag, in het midden alles wat je
        // over het boek moet weten — titel, knop en de synopsis er meteen
        //onder — en rechts de delen.
        //
        // Eén rij, geen twee. Met een synopsis in een eigen rij eronder stond
        // er naast de speelknop een lege strook: de rij erboven was zo hoog als
        // de kortste van de twee, en de rij eronder begon pas aan de onderkant
        // van de langste. Een lege ruimte waar een synopsis hoort is een
        // synopsis die je niet leest, en dit is precies de tekst die je nodig
        // hebt om te beslissen of je dit boep wil.
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        Grid.SetRow(body, 1);
        BookGrid.Children.Add(body);

        var cover = new Image
        {
            Width = 240,
            Height = 330,
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            SnapsToDevicePixels = true,
        };
        cover.Clip = new RectangleGeometry(new Rect(0, 0, 240, 330), 12, 12);
        _covers.Put(cover, book.Id, book.Cover, book.CoverV, _stop.Token);
        body.Children.Add(cover);

        // alles rechts van de omslag
        var info = new StackPanel { Margin = new Thickness(28, 0, 24, 0) };
        body.Children.Add(info);
        Grid.SetColumn(info, 1);
        Grid.SetRow(info, 0);

        info.Children.Add(new TextBlock
        {
            Text = book.Title,
            Style = (Style)FindResource("Text"),
            FontSize = 26,
            FontWeight = FontWeights.SemiBold,
        });
        info.Children.Add(new TextBlock
        {
            Text = book.Author,
            Style = (Style)FindResource("Dim"),
            FontSize = 15,
            Margin = new Thickness(0, 4, 0, 0),
        });

        var facts = new List<string>();
        if (!string.IsNullOrWhiteSpace(book.Series))
            facts.Add($"{I18n.T("series")}: {book.Series}"
                      + (book.SeriesNo > 0 ? $" {I18n.T("book", ("n", book.SeriesNo))}" : ""));
        if (!string.IsNullOrWhiteSpace(book.Narrator))
            facts.Add(I18n.T("narrator", ("name", book.Narrator)));
        if (book.Year is > 0) facts.Add(book.Year.Value.ToString());
        facts.Add(book.Parts == 1
            ? I18n.T("oneFile")
            : I18n.T("partsOf", ("n", book.Parts.ToString())));

        info.Children.Add(new TextBlock
        {
            Text = string.Join("  ·  ", facts),
            Style = (Style)FindResource("Faint"),
            FontSize = 12,
            Margin = new Thickness(0, 6, 0, 0),
        });

        // waar je was
        if (book.Progress is { } place && place.Position > 0 && !book.IsFinished)
        {
            var part = Math.Clamp(place.TrackIdx, 0, Math.Max(0, book.Parts - 1));
            var title = book.Tracks.Count > part ? book.Tracks[part].Title : "";
            info.Children.Add(new TextBlock
            {
                Text = I18n.T("whereYouWere",
                    ("where", title.Length > 0
                        ? I18n.T("partOf", ("n", part + 1), ("t", book.Parts), ("title", title))
                        : I18n.T("part", ("n", part + 1), ("t", book.Parts)))),
                Style = (Style)FindResource("Faint"),
                // Het goud zit in de kleur, niet in een stijl: `Accent` is een
                // penseel, en een penseel in `Style` gooit bij het openen van een
                // boek een InvalidCastException.
                Foreground = (Brush)FindResource("Accent"),
                FontSize = 13,
                Margin = new Thickness(0, 14, 0, 0),
            });
        }

        // de knop: wat er staat hangt af van of er een plek is, en van of het
        // boek af is. Drie teksten, drie situaties — en nooit een "Hervatten" op
        // een boek dat af is, want dat zou je de laatste seconden nog eens laten
        // horen en dan zomaar stoppen.
        var start = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
        var resumed = book.Progress is { Position: > 0 } && !book.IsFinished;
        var play = new Button
        {
            Content = book.IsFinished ? I18n.T("again") : resumed ? I18n.T("resume") : I18n.T("play"),
            Style = (Style)FindResource("Primary"),
        };
        play.Click += (_, _) => StartPlaying(book, book.IsFinished ? 0 : book.Progress?.TrackIdx ?? 0,
            book.IsFinished ? 0 : book.Progress?.Position ?? 0);
        // De naam erbij, met de titel van het boek erin. Zonder dit zegt een
        // schermlezer "▶ Afspelen" zonder te zeggen waarop, en op een pagina met
        // drie knoppen is driemaal "afspelen" zeggen net zo goed als niets zeggen.
        AutomationProperties.SetName(play, I18n.T(
            book.IsFinished ? "startThis" : resumed ? "resumeThis" : "playThis",
            ("t", book.Title)));
        // En een id dat niet van de taal en niet van de volgorde afhangt, zodat
        // er proefjes op kunnen klikken. Een naam is voor mensen, en verandert
        // met de taal; een id is voor gereedschap, en dat moet blijven werken.
        AutomationProperties.SetAutomationId(play, "BoekAfspelen");
        start.Children.Add(play);
        info.Children.Add(start);

        if (book.Parts == 0)
        {
            info.Children.Add(new TextBlock
            {
                Text = I18n.T("noParts"),
                Style = (Style)FindResource("Dim"),
                Margin = new Thickness(0, 12, 0, 0),
            });
        }
        else if (!resumed)
        {
            info.Children.Add(new TextBlock
            {
                Text = I18n.T("pressPlay"),
                Style = (Style)FindResource("Faint"),
                FontSize = 12,
                Margin = new Thickness(0, 8, 0, 0),
            });
        }

        // --- de synopsis, volledig, vlak onder de knop ----------------------
        //
        // In dezelfde kolom als de knop en niet eronder in een eigen rij, zodat
        // er geen gat tussen zit. Volledig, niet ingekort: dit is de tekst die
        // je leest om te weten of je dit boek wilt, en een synopsis die
        // afgekapt is, is een synopsis die net te kort is om te helpen.
        // Geen rechter marge: die zit al op de kolom zelf, anders staat er
        // dubbele ruimte tussen de synopsis en de delijst.
        var synopsis = new StackPanel { Margin = new Thickness(0, 26, 0, 0) };
        synopsis.Children.Add(new TextBlock
        {
            Text = I18n.T("synopsis"),
            Style = (Style)FindResource("Section"),
        });
        synopsis.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(book.Description) ? I18n.T("noDescription") : book.Description,
            Style = (Style)FindResource("Dim"),
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        info.Children.Add(synopsis);

        // --- de delen, in de rechterkolom -----------------------------------
        var parts = new StackPanel();
        parts.Children.Add(new TextBlock
        {
            Text = I18n.T("parts"),
            Style = (Style)FindResource("Section"),
            Margin = new Thickness(0, 0, 0, 6),
        });

        if (book.Parts == 0)
        {
            parts.Children.Add(new TextBlock
            {
                Text = I18n.T("noParts"),
                Style = (Style)FindResource("Faint"),
                FontSize = 12,
            });
        }
        else
        {
            for (var i = 0; i < book.Parts; i++)
            {
                var idx = i;
                var track = book.Tracks[i];
                // Niet bij een af boek: dan is er geen "hier" meer, en een
                // regel die zegt dat je bij het einde van deel één stond, terwijl
                // je helemaal klaar bent, is een regel die liegt.
                var here = !book.IsFinished && book.Progress is { } p && p.TrackIdx == i && p.Position > 0;
                var row = new Border
                {
                    Background = (Brush)FindResource(here ? "Panel2" : "Panel"),
                    BorderBrush = (Brush)FindResource(here ? "Accent" : "Line"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10, 8, 10, 8),
                    Margin = new Thickness(0, 0, 0, 5),
                    Cursor = Cursors.Hand,
                };
                var inside = new Grid();
                inside.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                inside.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var text = new StackPanel();
                text.Children.Add(new TextBlock
                {
                    Text = track.Title,
                    Style = (Style)FindResource("Text"),
                    FontSize = 12,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
                // Geen duur, dan geen duur. De site zet het etiket alleen als de server er
                // een gaf (`shelf.js`: `b.duration ? ... : ''`), en een `0:00`
                // zeggen is iets anders dan niets zeggen: `0:00` betekent "dit
                // deel is leeg", en dat is een leugen over een boek waar nog
                // een uur in zit. Een duur van 0 betekent hier "de server
                // kent hem niet", en dat staat nergens — je hebt het niet
                // nodig om verder te kunnen luisteren.
                var note = new List<string>();
                if (track.Duration > 0) note.Add(Clock(track.Duration));
                if (here) note.Insert(0, I18n.T("whereYouHere", ("time", Clock(book.Progress!.Position))));
                // En helemaal niets als er niets te zeggen valt: een lege regel
                // van twee pixels is hier alleen zichtbaar als de rij omhoog
                // schuift ten opzichte van de rij ernaast.
                if (note.Count > 0)
                {
                    var noot = new TextBlock
                    {
                        Text = string.Join(" · ", note),
                        Style = (Style)FindResource("Faint"),
                        Foreground = (Brush)FindResource(here ? "Accent" : "InkFaint"),
                        FontSize = 11,
                        Margin = new Thickness(0, 2, 0, 0),
                    };
                    // Zelfde reden als bij de klok in de balk: dit is het enige
                    // veld waarin staat hoe lang dit deel is, en een proefje
                    // moet dat kunnen lezen zonder te zoeken naar het eerste
                    // uurteken in het venster. Er is er per rij één, dus de app
                    // hoeft er geen nummer aan te geven.
                    AutomationProperties.SetAutomationId(noot, "DeelNoot");
                    text.Children.Add(noot);
                }
                inside.Children.Add(text);

                var label = new TextBlock
                {
                    Text = I18n.T("playIt"),
                    Style = (Style)FindResource("Dim"),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(label, 1);
                inside.Children.Add(label);
                row.Child = inside;

                var button = new Button
                {
                    Content = row,
                    Style = (Style)FindResource("Plain"),
                    Padding = new Thickness(0),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                };
                // De rij is één knop van tekst en teken, en zo'n knop heeft
                // standaard geen naam: een schermlezer zegt dan "knop" en niets
                // anders. De titel van het deel erin zetten, want dat is wat je
                // moet horen om deze knop van de volgende te onderscheiden.
                AutomationProperties.SetName(button, I18n.T("playPart", ("t", track.Title)));
                button.Click += (_, _) => StartPlaying(book, idx, here ? book.Progress!.Position : 0);
                parts.Children.Add(button);
            }
        }
        body.Children.Add(parts);
        Grid.SetColumn(parts, 2);
    }

    private async Task ToggleHeart(BookDetail book, Button heart)
    {
        var on = !_hearts.Contains(book.Id);
        try
        {
            await _collection.MarkFavourite(book.Id, on, _stop.Token);
            if (on)
            {
                _hearts.Add(book.Id);
                _favourites ??= new List<Book>();
                _favourites.Add(book.AsBook());
            }
            else
            {
                _hearts.Remove(book.Id);
                _favourites?.RemoveAll(f => f.Id == book.Id);
            }
            heart.Content = on ? "♥" : "♡";
            heart.Foreground = (Brush)FindResource(on ? "Red" : "InkFaint");
            heart.ToolTip = I18n.T(on ? "inYourFavourites" : "addToFavourites");
            AutomationProperties.SetName(heart, I18n.T(on ? "inYourFavourites" : "addToFavourites"));
            Say(I18n.T(on ? "favouriteAdded" : "favouriteRemoved", ("title", book.Title)));
        }
        catch (Fault f)
        {
            Say(f.Text);
        }
    }

    /// Zet de speler op dit boek en dit deel en laat het spelen.
    ///
    /// Dit is het ene plek waar "spelen" begint. Overal anders — het opstarten,
    /// de thuispagina, de planken — wordt er niets gezet en niets gestart.
    private void StartPlaying(BookDetail book, int part, int seconds)
    {
        _player.Load(book, part, seconds, autoplay: true);
        Bar.Visibility = Visibility.Visible;
        BuildBar();
    }

    private Task GoBackFromBook()
    {
        if (_bookCameFrom == Where_.Home) return GoHome();
        return GoShelves();
    }
}
