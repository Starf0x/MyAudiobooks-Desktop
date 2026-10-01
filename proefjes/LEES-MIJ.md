# Proefjes

Hier staan de scripts waarmee de app van buiten af is nagekeken. Ze zijn er
gekomen omdat er geen andere manier was: er is geen testproject, en een scherm
zonder beeld kun je niet lezen. Elk script doet één ding en zegt wat het heeft
gezien, zodat een claim in deze app te controleren is zonder iemand te
vertrouwen die het zegt.

Ze kijken op drie manieren:

- **wat de server zag** — `requests.log` in deze map, geschreven door de
  proefserver;
- **wat er op het scherm staat** — via UI Automation (`uia.ps1`), dus de
  teksten en de knoppen zoals een schermlezer ze leest;
- **of het misgaat** — `fouten.log` naast de andere bestanden van de app, en het
  feit of die er is. Zie hieronder: de proefjes hebben daarvoor een eigen map,
  zodat ze die van je echte installatie niet raken.

## Draaien

```powershell
.\alle.ps1                              # alles, en een oordeel per proefje
.\alle.ps1 -Alleen hervatten            # één proefje, of er een paar
.\voortgang-van-server.ps1               # één proefje, los
.\proef-bewijst-zichzelf.ps1             # kijkt of dat proefje iets meet
```

`proef-bewijst-zichzelf.ps1` is geen proefje maar gereedschap, en hij is
destructief: hij zet de tweede voortgang-boekhouding terug in de app om te zien of
`voortgang-van-server.ps1` dat merkt. Zie "Voortgang komt van de server"
hieronder. Draai hem niet naast `alle.ps1`.

`alle.ps1` zet de BOM op alle scripts (§ BOM hieronder), start en stopt de
proefserver rond elk proefje, en zoekt in de uitvoer naar `MISLUKT`, `ONGELDIG`
en `NIET GEVONDEN:`. Die woorden staan nergens anders, dus dit is de plek waar je
in één oogopslag ziet of er iets mis is.

De dubbele punt bij `NIET GEVONDEN:` is er niet voor de sier. `zoeken.ps1`
schreef "gezegd wat er niet gevonden is" toen het wél goed ging, en dat maakte
het proefje rood op zijn eigen succes. Alleen de woorden die een probleem
betekenen eindigen op een dubbele punt: `NIET GEVONDEN: id=…` uit `uia.ps1` en
`TITEL NIET GEVONDEN: …` uit `open.ps1`.

Los draaien mag ook, en dan is er één regel die geldt: **elk proefje dat de
server nodig heeft start hem zelf** (via `StartProefserver` in `hulp.ps1`), en
`requests.log` wordt bij elke start leeg gemaakt. Twee proefjes tegelijk kan
dus niet, en een proefje dat de server laat staan bepaalt de uitkomst van het
volgende.

De eerste keer aanmelden doet elk proefje zelf: wachtwoord `probe`, elke
gebruikersnaam.

## De proefjes blijven stil en staan niet op je scherm

Een proefronde duurt minuten, speelt echte boeken af en opent tientallen
vensters. Dat is een reden om er niets van te merken terwijl je aan het werk
bent, en daarom gebeurt het in `hulp.ps1`, in `StartDeApp`:

- **stil** — `ZetGeluidUit` schrijft `muted: true` en `volume: 0` in
  `state.json` vóór het starten. Dat is de gewone app-instelling
  (`Store.SetSound`), geen proefschakelaar in de app: de app leest dat bestand
  bij het opstarten en speelt daarna stilhet. `ControleerGeluidUit` leest het
  bestand daarna terug en zegt of de app het geluid echt heeft uitgezet —
  aanmelden herschrijft `state.json`, dus wat er dan in staat is wat de app
  ervan heeft gemaakt, en niet wat het proefje heeft neergezet. Drie proefjes
  melden zich zelf aan in plaats van via `StartEnMeldAan`, en die roepen
  `ControleerGeluidUit` zelf aan.
- **minimaal** — de app start met `-WindowStyle Minimized`. Het venster is dan
  vanaf de eerste seconde geminimaliseerd (`IsIconic`), en UI Automation leest en
  klikt daarna gewoon door: `GateName`, `GateGo` en de boekkaarten werken
  allebei. `StartDeApp` controleert dat na het starten en zegt het hardop als het
  toch op je scherm ligt.

Eerder werd het venster ná het opstarten geminimaliseerd. Dat werkte, maar dan
had het al een seconde open op tafel liggen, en dat is precies de seconde die
iemand stoort. Vandaar meteen al geminimaliseerd beginnen.

Let op: de geluidsregeling van de app blijft gewoon werken. Alleen tijdens een
proefje staat hij op stil, en dat is op twee manieren na te gaan: `state.json` geeft
`"muted": true` aan, en de geluidsknop op het scherm zegt "Sound off" (of "Geluid
uit"). Die knop heeft daarom een `AutomationId`: `SpelerDempen`.

Dat het teken van die knop de stand volgt is een reparatie, geen extra. De site
doet het (`player.js:179`: `audio.muted || !audio.volume ? '🔇' : '🔊'`) en de app
niet, dus er stond tijdens een proefje een "🔊" terwijl er niets te horen viel.
Nu zegt de knop wat er is, en zegt zijn naam het ook — voor een schermlezer, en
voor een proefje dat het wil nagaan.

`ControleerDempenOpHetScherm` werkt pas als er een boek open is, want de
spelerbalk staat dan pas op het scherm. Daarom staat die controle in
`voortgang-van-server.ps1` ná het afspelen en niet meteen na het aanmelden; de
bestandscontrole (`ControleerGeluidUit`) werkt meteen.

## De proefjes raken je echte installatie niet

`hulp.ps1` zet `MABC_DATA` op `%TEMP%\proefjes-mabc`, en `App.Choose()` in de app
leest dezelfde variabele. Cookie, `state.json` en beide logbestanden van een
proefje komen daar terecht, niet in `%LOCALAPPDATA%\My Audiobooks`.

De voortgang per boek staat daar sowieso niet: die wordt vanaf de server
gelezen en daar bewaard. Zie hieronder, bij "Voortgang komt van de server".

Dat is geen netheid. Toen die regel er nog niet was, zetten de proefjes hun
proefserver op en schreven hun cookie en hun voortgang dwars door die van de
echte installatie heen — en de sessie van de gebruiker was weg. Er is geen kopie
van bewaard, dus die moet opnieuw: `state.json` en `cookie.bin` in
`%LOCALAPPDATA%\My Audiobooks` zijn opnieuw aanmelden.

De voortgang per boek begint op nul omdat hij daar sowieso niet meer staat: die
komt van de server, en de proefjes draaien tegen hun eigen proefserver. Op de
echte server staat nog steeds wat je tot nu toe hebt gehoord.

Wil je de app met de echte server proberen, start hem dan zelf zonder `MABC_URL`
én zonder `MABC_DATA`, anders wijst hij naar de laatste proefserver.

## Wat elk proefje meet

| proefje | de belofte |
|---|---|
| `geen-mp3-route.ps1` | de server kent `/api/mp3` niet — dat is de standaard, want zo is de draaiende server — en er speelt tóch geluid, via `/api/stream`, en er staat geen klacht over mp3 op het scherm |
| `hervatten.ps1` | een boek waarvan de server de duur niet kent: geen `0:00` op het scherm, de klok loopt, "terug" springt terug in de tijd, en heropenen begint waar je gebleven was |
| `voortgang-van-server.ps1` | de voortgang komt van de server en niet uit een eigen lijst: hem op de server gezet vóór de app start geeft "Resume" en de juiste plek, en in `state.json` komt geen voortgang |
| `niet-mp3.ps1` | een deel dat als m4a op de schijf ligt: de app zegt wat de server stuurde (`audio/mp4`) en wat er mee te doen is (omzetten in de webinterface), en er speelt niets |
| `geen-geluid.ps1` | als geen van beide geluidsroutes werkt, zegt de app wat **beide** servers antwoordden, en er speelt niets en er wordt geen voortgang bewaard |
| `boek-dat-omgezet-moet-worden.ps1` | de server antwoordt 409 met voortgang; de app zegt zichtbaar dat hij wacht en begint vanzelf zodra het klaar is, zonder klikken |
| `aanmelden-en-afspelen.ps1` | aanmelden werkt, de thuispagina staat er, een boek opent **zonder** één verzoek om geluid, en tijdens het spelen komen er geluids- en voortgangsverzoeken |
| `boek-uit.ps1` | een boek van één deel loopt uit; de voortgang eindigt op de volle lengte met `KLAAR`, de statistiek telt mee, en het boek verdwijnt uit "Verder luisteren" |
| `geen-favorieten.ps1` | bij een 404 op `/api/favourites` verdwijnt het hart en zegt de app er niets over: geen functie is geen fout |
| `zoeken.ps1` | een zoekopdracht gaat naar `/api/search` (niet naar een genre of auteur), het gevonden boek staat er, en een zoekopdracht zonder uitkomst zegt dat in plaats van stil te verdwijnen |
| `een-proces.ps1` | precies één proces, nul kindprocessen, geen WebView2 of Edge of node **van deze app**, één raam, en `state.json` plus `cookie.bin` in de map van de app |

`proef-bewijst-zichzelf.ps1` staat niet in deze lijst omdat hij geen belofte over
de app meet, maar over een proefje: kijkt of `voortgang-van-server.ps1` een
MISLUKT geeft wanneer de tweede voortgang-boekhouding weer in de app staat. Zie
"Voortgang komt van de server" hieronder.

## Wachten op een melding, in plaats van een tijd slapen

Een melding in deze app staat **zeven seconden** zichtbaar (`MainWindow.Say`, en
bewust langer dan de 2,6 van de site, want sommige klachten moet je kunnen
overtypen). Een proefje dat na zeven seconden kijgt, leest dan een scherm waar de
melding al van af is.

`geen-geluid.ps1` deed precies dat, en concludeerde twee keer dat de app niets
zeegt, terwijl dezelfde melding twee seconden na de klik gewoon op het scherm
stond. Het proefje meet dan de klok in plaats van de app.

Daarom staat er `WachtOpTekst` in `hulp.ps1`: die leest elke 400 ms tot de
regel er staat, zegt hoe lang dat duurde, en geeft na 20 seconden een lege lijst
terug — zodat een melding die nooit komt een echte fout is en geen ongeluk.
`niet-mp3.ps1` sliep zes seconden en won daarmee de race per toeval; die staat ook
op `WachtOpTekst`.

Overigens is zeven seconden een grens die in het programma staat, niet in het
proefje. Wil je de app langer laten zeggen, dan is `MainWindow.Say` de plek —
en dan blijft `WachtOpTekst` gewoon waar hij staat.

## De twee geluidsroutes, en waarom de proefserver er één heeft

De webspeler doet `audio.src = /api/stream/${t.id}` (`player.js:61`). Dat is de
enige geluidsroute op de draaiende server. `/api/mp3/<trackId>` — waarmee de app
eerder begon — staat alleen in de andere kopie van de server, in
`MyAudiobooks/server/index.js`, en die kopie draait niet.

De proefserver heeft daarom **geen** `/api/mp3`, tenzij een proefje erom vraagt
met `proefserver-aanzetten.ps1 -Mp3Route`. Dat is omgekeerd met wat het was: toen
had de proefserver wél `/api/mp3` en de echte server niet, en dus draaiden alle
proefjes tegen een server die niet bestaat. `boek-dat-omgezet-moet-worden.ps1` is
het enige proefje dat de mp3-route nodig heeft, en hij start hem zelf.

`/api/stream` geeft het bestand zoals het op de schijf ligt, met de
`Content-Type` die bij de extensie hoort, en met `Range`. Dat is waar de app op
afspielt als de mp3-route er niet is, en het is waarom de app uit de header
leest of er mp3 komt: er is één decoder, en de site heeft er zes.

## BOM

Elk script heeft een BOM (een `EF BB BF` aan het begin) en daarom staat er
`bom.ps1`.

Windows PowerShell leest een `.ps1` zonder BOM in de ANSI-codepagina van de
machine in plaats van UTF-8. Op deze machine is dat cp1252, en daar is byte
`0x94` een sluitend aanhalingsteken. Een em-dash `—` is in UTF-8 het drietal
`E2 80 94`, en dat derde byte is dus een `"`. Gevolg: het script breekt af met
"The string is missing the terminator" op een regel die er volkomen normaal
uitziet, en de fout wijst naar de regel erna.

Dat is geen theorie: het kostte `geen-mp3-route.ps1`. `bom.ps1` zet de BOM en
parseert daarna elk script, want een script dat niet parseert doet niets en zegt
niets.

`bom.ps1` doet nog iets tweeds: het controleert of elk proefje `MISLUKT` schrijft
en niet een eigen variant. Drie proefjes schreven een verkeerd gespeld oordeelwoord
— zeven keer, in precies de twee proefjes die deze ronde moesten bewijzen — en
`alle.ps1` beslist groen of rood door in de uitvoer naar dat woord te zoeken. Een
proefje met een typefout in zijn eigen oordeelwoord is geen proefje met een
typefout, maar een proefje dat nooit rood kan worden. Twee bestanden (`bom.ps1`
en `alle.ps1`) zijn daarvan uitgezonderd, en die staan bij naam in de lijst, want
ze bevatten het woord en het zoekpatroon om uit te leggen waarom.

## Aanmelden per proefje

Elk proefje start de app **en meldt zich zelf aan** (`StartEnMeldAan` in
`hulp.ps1`), en controleert daarna of het aanmeldingsscherm echt weg is.

Niet "de aanmelding van de vorige keer blijft staan", zoals twee proefjes het
deden. Die aanname klopte zolang de proefserver één keer voor de hele ronde
gestart werd. Nu start elk proefje hem zelf, en de proefserver houdt zijn sessies
in het geheugen (`const sessions = new Set()` in `fixtures/app-probe.mjs`), dus
na een herstart kent hij geen enkele cookie meer. De twee proefjes die op een
geleende sessie leunden zagen het aanmeldingsscherm als "de titel is er niet" en
als "er speelt niets", en meldden klachten over het verkeerde onderwerp.

Een proefje dat niet aangemeld is, meet niets. Dat klinkt vanzelfsprekend en is
het niet: het meet dan iets anders, namelijk het aanmeldingsscherm.

## Voortgang komt van de server

De plek in een boek staat op de collectie-server, en nergens anders. De app
leest hem met elke schermpagina die een boek toont, en schrijft hem terug naar
`/api/progress` — om de vijf seconden, bij het pauzeren, en aan het eind van een
deel. `state.json` bevat geen voortgang, en daar is geen `place` meer in.

Dat is een keuze, en de reden is de klacht waar dit programma om begonnen is.
"Verder luisteren" komt van de server. Toen stond er daarnaast, als de plank leeg
was, een boek dat de app zelf onthouden had, met een regel eronder dat de plek
alleen hier stond. Dat leek zorgvuldig, en het was een tweede boekhouding. Waar
de twee het oneens waren — en dat gebeurde, want de app bewaart om de vijf
seconden en de server kan daar tussenuit vallen — zei de app niets. En het
ergste geval is niet dat ze het oneens waren: het is dat de app dan een plek
toont die de server niet kent. Je drukt op "Hervatten" en begint op nul, en dat
leest op het scherm als "hervatten werkt niet".

Dat is de derde oorzaak naast de twee uit `ONDERZOEK-AUDIO.md` (de
`/api/mp3`-route en de `OneLine`-bug), en de lastigste om te vinden, want de app
gedroeg zich in dit opzicht precies zoals gevraagd.

### `voortgang-van-server.ps1`

Meet dit, en wel zo dat de app er niet langs kan:

1. voortgang op de server zetten voordat de app gestart is, met
   `Invoke-RestMethod` en een eigen sessie — dus nog voor er een venster is;
2. de app starten en aanmelden, en `state.json` lezen;
3. het boek openen: de knop moet "Resume" heten, want dat komt uit het
   `progress` van het antwoord op `/api/books/:id`;
4. afspelen, en meten dat de klok op 37 seconden begint en niet op 0;
5. in `requests.log` zien dat de app zijn eigen voortgang naar `/api/progress`
   stuurt;
6. `state.json` nog eens lezen, nu terwijl er wél iets te bewaren valt.

Stap 6 is de stap die het verschil maakt, en dat is gemeten in plaats van
verondersteld. Met de tweede boekhouding weer in de build geeft stap 2 "goed" en
alleen stap 6 een MISLUKT. Stap 2 is dus op zichzelf geen bewijs, en dat staat er
ook zo bij.

Dat terugdraaien om het proefje te testen deed twee keer iets anders dan
verwacht, en beide zijn hier vastgelegd omdat ze de hele opzet ondermijnen:

- **De sabotage compileerde niet.** `from = kept.Position`, terwijl die variabele
  er niet meer was. Publish mislukte, dus het proefje draaide tegen de oude
  binary en zei "groen". Een proefje dat groen zegt omdat de build mislukte is
  erger dan geen proefje.
- **`Copy-Item` houdt de oude tijdstempels.** Na het terugzetten van de bronnen
  had `MyAudiobooks.dll` een nieuwere `LastWriteTime` dan de bronbestanden, dus
  MSBuild besloot dat alles klopte en herverpakte de gesaboteerde dll. De
  bronnen waren schoon, de exe niet. De uitkomst van dat proefje was dus over de
  vorige build, niet over de huidige.

Dat is nu een script: `proef-bewijst-zichzelf.ps1`. Het zet de tweede
boekhouding terug in `Store.cs` en `Player.cs`, bouwt, draait
`voortgang-van-server.ps1`, en zegt dat het proefje een MISLUKT had te geven.
Het zet de bronnen terug, ook als er iets misgaat, en stopt met een fout als de
build mislukt — want een proefje tegen de vorige exe zegt niets. Draai het niet
naast `alle.ps1` en niet terwijl je aan die bestanden werkt: het schrijft er in.

Dat het proefje het ziet is geen eigenwijs oordeel van het script, maar dat is
niet te vermijden zonder dat het script het proefje zelf kan lezen — en het leest
het. Het eist bovendien dat stap 3 doorlopen is, anders zegt het dat er niets te
beoordelen valt.

## Drie proefjes die hebben gelogen, en één die niets beweerde

Vastgelegd, want ze gebeuren niet nog een keer.

**Een proefje dat alleen telt.** `aanmelden-en-afspelen.ps1` typte `mp3: 0
voortgang: 0` af en sloot daarna af met de regel "(geen foutenlijst)". Er
speelde dus niets, en het proefje zei niets. Nu beoordeelt het die twee aantallen
en zegt het hardop wanneer ze te laag zijn.

**Een schakelaar die bleef hangen.** `geen-mp3-route.ps1` zette
`APP_PROBE_NO_MP3=1` en ruimde die niet op. Elke proef daarna startte de
proefserver mét die schakelaar, dus zonder mp3-route — en het proefje hierboven
zag dus de 0. Twee fixes: het proefje ruimt zijn schakelaar op, en
`proefserver-aanzetten.ps1` zet beide schakelaars uit voordat hij de server
start.

**Een proefserver die uit stond.** Drie proefjes na elkaar, en de eerste twee
waren groen zonder één seconde geluid. De oorzaak: de server moest apart gestart
worden, en dat gebeurde niet. Het proefje dat het hoort te merken vroeg het
wél — maar bond het antwoord aan een variabele, en daarmee slokte het zijn eigen
uitleg op. Dus: de uitkomst is nu een `$true` of `$false` en de tekst gaat met
`Write-Host`, en elk proefje start de server zelf.

**Een proefje dat de verkeerde route beoordeelde.** `geen-mp3-route.ps1` beoordeelde
een 404 op `/api/mp3` en concludeerde dat de app het moest zeggen. Dat klopte,
maar het was de verkeerde vraag: de app had op `/api/stream` kunnen spelen, want
dat is wat de site doet. Het proefje meet nu dat er geluid klinkt zonder mp3-route.

**Een proefje dat een ander programma de schuld gaf.** `een-proces.ps1` keek welke
processen er in zijn venster van veertien seconden waren begonnen, en noemde alles
met `msedge|WebView2|chrome|node|electron` een meegeslepen browser. Op deze
machine starten in die tijd de WebView2-processen van Teams, Grammarly, Outlook,
FortiClient en Superhuman. Uitkomst: `MISLUKT: er is een browser of een
node-proces bijgekomen: 1 x msedgewebview2` — terwijl het app-gedrag wél correct
was, want het proefje meldde zelf `kindprocessen: 0`. Er was geen WebView2 bij.

De vraag is nu "kwam er een browserproces op **dat van deze app is**", en dat gaat
via `ParentProcessId` omhoog. De lijst van alles wat er tegelijk begon blijft
staan, want die is niet het probleem; hij staat nu naast de vraag wie er van wie
is, in plaats van ervoor.

Bij het schrijven van datgene twee dingen misgegaan die beide het antwoord
onbetrouwbaar maakten zonder dat het rood leek:

- **de sleutel van de hashtabel.** `Get-Process` geeft een `Int32` en
  `Get-CimInstance` een `UInt32`, en PowerShell vindt `1234` (Int32) niet bij de
  sleutel `1234` (UInt32). De eerste versie zei "bij de app: 0" terwijl er
  MyAudiobooks op die lijst stond: de app telde niet mee, dus de controle deed
  niets;
- **`$meegelopen` heette nog naar de oude lijst.** Na het herzien van de vraag
  stond `$meegelopen` nog op de volledige `$new`, en het oordeel zou dus
  onveranderd rood blijven.

De algenere regel: **een controle die de zaak die je meet niet eens terugvindt,
meet die zaak niet.** Dat hoort hier naast de andere twee, want in alle drie de
gevallen was de uitvoer van het proefje waar en de conclusie onwaar.

## `uia.ps1` en `open.ps1`

Gereedschap, geen proefjes.

`uia.ps1` heeft deze acties:

| actie | wat het doet |
|---|---|
| `dump` | de hele boom, met ids, namen en maten |
| `texts` | alle teksten |
| `labels` | alle teksten met hun linkerrand, om rijen op te meten |
| `text` | de tekst van **één** element, op id |
| `textsById` | alle elementen met één id, één per regel |
| `buttons` | alle knoppen, met id en naam |
| `invoke` | klik op id (of op naam als er geen id is) |
| `invokeName`, `invokeLike` | klik op een naam, of op een naam waar een stukje tekst in zit |
| `invokeIndex` | klik op de knop met nummer — alleen voor de poort, nooit voor een gedrag |
| `type`, `value` | tekst in een veld zetten, en de waarde lezen |

`text` en `textsById` zijn er gekomen omdat `texts` en `labels` te breed zijn om
iets te meten: de eerste regel die op een klok lijkt is de titel van een boek dat
"0:00" in de naam heeft, of de notitie van een ander deel. Met een id is er maar
één element dat het kan zijn.

`labels` is er naast `texts` bijgekomen om rijen te kunnen meten: `texts` geeft
alleen de woorden, en een rij begint op een pixel — niet op een gevoel. Daarmee
kwam het verschil van 11 pixels tussen een rij mét en zonder pijltje boven, en dat
is precies de maat die `#genres .twist { width: 11px }` voorschrijft.

Klikken gebeurt op een **id**, nooit op een volgnummer en nooit op een naam. Een
naam verandert met de taal van Windows, en een volgnummer verandert zodra er een
knop bij komt. Toen er een knop bij kwam — de kop van de rechterkolom — klopte
het proefje op nummer 5 de genre-rij aan, meldde "0 mp3-verzoeken" en concludeerde
dat afspelen kapot was, terwijl het gewoon werkte. Lezen mag alleen op id, om
dezelfde reden.

De ids die de app nu heeft: `BoekAfspelen`, `KaartAfspelen`, `DeelNoot`,
`GenreKiezen`, `GenreKlappen`, `TerugNaarPlanken`, `SpelerAfspelen`,
`SpelerTijd`, `SpelerDuur`, `SpelerTerug`, `SpelerVooruit`, `SpelerVorigDeel`,
`SpelerVolgendDeel`, `SpelerDempen`. `SpelerDempen` is de jongste en de enige die
er bij is gekomen om een stand te kunnen aflezen; de andere zijn er om op te
klikken.

## Tegen de echte server

Alle proefjes wijzen de app met de omgevingsvariabele `MABC_URL` naar de
proefserver. Laat je die variabele weg, dan gebruikt de app de echte collectie
uit `state.json` — en dan kun je de proefjes draaien om te zien of de echte
server het ook doet. De wachtwoorden verschillen dan; vul zelf in wat geldt.

Let daarbij op `MABC_DATA`: `hulp.ps1` zet die, dus binnen een proefje
gebruikt de app altijd de map van het proefje en nooit die van je installatie,
ongeacht welke server je hem geeft. Voor een handmatige proef tegen de echte
server start je de app dus gewoon vanuit Verkenner, of zet je die variabele
leeg.

Wat er op de echte server te meten valt, staat in `ONDERZOEK-AUDIO.md`, onder
het stuk over de route die daar niet is. De vraag die daar openstond — of
`/api/stream/<spoor>` daar `audio/mpeg` teruggeeft voor jouw boeken — is met de
proefjes niet te beantwoorden, want daar draaien ze tegen een proefserver. Dat
staat er ook zo, en niet als een aanname: het staat als een openstaande vraag
met de handeling die het beantwoordt.
