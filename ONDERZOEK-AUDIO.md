# De speler: wat kan een native C# app, en wat niet

Vastgelegd op 30 september 2026, vóór het bouwen van het venster. Elk getal hieronder
is gemeten, niet geschat: de proef (`audioprobe`) telt de bytes bij de netwerklaag en
telt de verzoeken mee.

## "Hervatten doet niets" had drie oorzaken

De klacht waar dit project mee begon is niet in één regel te repareren geweest. Er
zaten drie oorzaken in, op drie plekken, en geen van de drie was in het afspelen
zelf te vinden:

1. **de mp3-route die er niet is** — de app vroeg `/api/mp3/<spoor>` en de draaiende
   server heeft alleen `/api/stream/<spoor>`. Zie "De route die er op de draaiende
   server niet is" hieronder;
2. **de duur van 0** — `scan.js` schrijft 0 als een mp3 geen Xing-kop heeft, en de app
   behandelde dat als "leeg" in plaats van "onbekend". Ook in dat hoofdstuk;
3. **twee boekhoudingen** — de app bewaarde de voortgang ook op schijf, naast de
   server, en waar die twee het oneens waren zei hij niets. Zie "De derde oorzaak"
   onderaan.

Bij elk van de drie deed de app precies wat gevraagd was. Dat is wat ze gemeen
hebben, en het is waarom ze alle drie te vinden waren: niemand van de drie is te
zien in het gedrag van het afspelen, want het afspelen deed het.

Het overkoepelende punt staat onderaan en geldt voor alle drie: **een app die twee
bronnen heeft, spreekt zichzelf tegen zonder het te merken, en de gebruiker ziet
alleen het gevolg — "er gebeurt niets".**

## Waarom dit er was

Electron trok vier processen. Eén proces kan alleen door van Chromium af te stappen, en
dus door de pagina van HTML/CSS/JS naar WPF te herschrijven. Dat is de reden dat dit
project bestaat. Het risloot daarbij is de speler: Chromium speelt mp3, m4a, flac, ogg
en opus allemaal af, en WPF niet vanzelf.

## De opzet

- `NAudio 3.1.0`, met losse pakketten `NAudio.WinMM` (uitvoer), `NAudio.Wasapi`
  (uitvoer), `NAudio.Vorbis 3.0.0` (ogg/flac). In NAudio 3 zitten de decoder en de
  uitvoer niet meer in de kern.
- Testbestanden van 30 minuten, gemaakt met ffmpeg (`C:\Windows\ffmpeg.exe`):
  mp3 6,84 MB · ogg 4,68 MB · m4a 20,9 MB · flac 75,7 MB.
- Een eigen minimaal filesysteem met Range, omdat `HttpListener` zonder
  beheerdersrechten geen poort opent. Het telt verzoeken en bytes.
- Drie manieren om hetzelfde bestand te openen, en de eerste die werkt wint:
  NAudio's eigen lezer, dan Media Foundation.

## De uitkomst

| formaat | lezer | openen | 1,5 s geluid | sprong naar 20 s |
|---|---|---|---|---|
| mp3 | NAudio | 197 KB | 0 KB (in de buffer) | **9 ms, 229 KB, 7 verzoeken** |
| ogg (vorbis) | NAudio.Vorbis | 98 KB | 98 KB | **17 ms, 166 KB, 6 verzoeken** |
| flac | Media Foundation | 164 KB | 33 KB | 12 ms, maar op 30-minuten hangt het |
| m4a / m4b (aac) | — | — | — | **kan niet** |
| wma | — | — | — | **kan niet** |
| opus | — | — | — | **kan niet** |

mp3 en ogg zijn daarmee echt streamend: een sprong van drie seconden in een boek van
zes megabyte kost een kwart megabyte. Er wordt niets gedownload en er komt niets op
schijf.

## Wat er misging, en wat het opleverde

Drie fouten in de proef, alle drie in de stroom, geen van alle in de decoder. Ze zijn
hier neergezet omdat ze in de echte app terugkomen als ze niet worden opgelost.

**1. De lees-thread gaf op bij het einde van het bestand.** mp3 en ogg hingen 45
seconden lang. Oorzaak: de mp3-zoeker springt vier bytes terug om een
synchronisatieteken te zoeken. Bij het einde van het bestand stond de thread op, dus
na die sprong was er niemand meer die iets binnenhaalde — de speler wachtte op iets dat
niet meer kon komen. Opgelost: de thread wacht nu tot de lezer doorspoelt in plaats van
te stoppen.

**2. Bij elke sprong ging de hele buffer weg.** Eén sprong van 4 bytes kostte toen een
verse 64 KB. Meten over een halfuur mp3: doorspoelen naar 20 s trok **216 MB over 4683
verzoeken** — 250× de bestandsgrootte, in een app die uitdrukkelijk niets downloadt.
Opgelost: een sprong gooit alleen de stukken weg die helemaal vóór de nieuwe plek
liggen; het stuk dat de nieuwe plek bevat blijft staan. Zelfde meting daarna: 229 KB.

**3. Terug springen vóór het begin van een stuk gaf een negatieve offset**
(`sourceIndex '-6'`). Opgelost met een duidelijke regel: springt de lezer terug voor
alles wat binnen is, dan is geen van die stukken bruikbaar en gaan ze allemaal weg.

Daarnaast twee dingen die de proef misleidend maakten en die ik heb gerepareerd: de
proefserver sloot elke verbinding (`Connection: close`), waarna de verbindingspool van
HttpClient dode contacten vasthield; en de proef mat een tweede stroom die niemand
laste.

## Waar het nu op uitloopt

Er is in .NET geen decoder die m4a, wma of opus boven op een netwerkstroom kan lezen.
Media Foundation weigert ze boven op een stroom (`0x8000FFFF`, `0x8000FFFF`) terwijl
dezelfde bestanden wél uit het geheugen wel lukken — het is dus de stroom die geweigerd
wordt, niet de codec. Dat is een echte beperking en geen bug die nog te repareren valt.

En het gaat niet om zeldzaamheden: `server/convert.js` bestaat precies om ogg en m4b
naar mp3 te maken, en in de commentaar staat dat er zulke boeken zijn. De collectie
bevat dus audio dat een native C# app niet kan streamen.

## De beslissing: alleen mp3

Er is voor gekozen de app **alleen mp3 te laten spelen**. Dat is het antwoord zonder
bijlagen: geen native DLL, geen tijdelijk bestand, niets gedownload, en geen tweede
decoder waarvan de fouten onbekend zijn.

Alles wat geen mp3 is, wordt door de server van mp3 voorzien. Dat is geen plan meer,
het is gebouwd en gemeten:

- `server/rendition.js` maakt van één bronbestand één mp3, één keer, en bewaart die in
  een map `.mp3/` naast het boek. De punt vooraan is niet cosmetiek: `scan.js` slaat
  punt-mappen over, dus de cache wordt nooit als een boek gezien.
- `GET /api/mp3/:trackId` in `server/index.js` geeft die mp3. Is het spoor al mp3, dan
  komt het origineel onveranderd terug. Is het m4a, dan wordt het eerst gemaakt en
  krijgt de app ondertussen `409` met hoe ver hij is, zodat er iets te tonen is in
  plaats van stilte.
- **Range werkt**, want er wordt een bestand van schijf gestuurd. Dat is geen
  bijdetail: doorspoelen in de app ís Range, en een live buis zou een speler zijn die
  niet kan overslaan.
- De duur klopt tot op de tiende seconde, dus een opgeslagen positie blijft kloppen.
- `/api/stream/:trackId` is ongemoeid: de webspeler en Home Assistant blijven werken.

`fixtures/rendition-probe.mjs` draait dit van begin tot eind tegen een echte tijdelijke
collectie, en controleert al die punten afzonderlijk.

Eén ding dat onderweg misging en blijft staan, want het is zo'n fout die terugkomt:
`send` weigert elk pad met een punt erin en antwoordt 404. De cachemap heet `.mp3`, dus
de route gaf een bestaand bestand terug en serveerde het toch niet — zonder dat er
iets mis leek. Opgelost met `dotfiles: 'allow'`, met een commentaar erbij waarom dat
hier geen wildcard is.

## Wat dit voor de app betekent

- Eén decoder: `Mp3FileReaderBase` van NAudio. Gemeten: openen kost 197 KB, doorspoelen
  naar 20 s kost 229 KB in 9 ms, en daar zit geen schijfwerk tussen. Dat is echt
  streamen.
- Geen ogg, geen flac, geen m4a, geen wma, geen opus, en geen Media Foundation als
  reserve. Dat is bewust: een tweede decoder die half werkt geeft een speler die soms
  geluid geeft en soms niet, en dat is erger dan een die zegt wat hij niet kan.
- De app vraagt `/api/mp3/…`. Kan de server dat niet, dan valt hij terug op
  `/api/stream/…` — de route die `player.js:61` gebruikt — en alleen als die ook
  niets geeft zegt hij iets. Zie hieronder.
- De app toont de `409` als een voortgangsregel: "dit boek wordt omgezet naar mp3".

## De route die er op de draaiende server niet is (1 oktober 2026)

Boven staat "de app vraagt `/api/mp3/…`". Dat klopte voor `MyAudiobooks/server`, en
niet voor de server die draait. Het verschil is één regel in twee bestanden, en
die regel kostte een avond:

```
MyAudiobooks/server/index.js:1101   app.get('/api/stream/:trackId', …)
MyAudiobooks/server/index.js:1117   app.get('/api/mp3/:trackId', …)     ← alleen hier
my_audiobooks_collection/server/index.js:1157   app.get('/api/stream/:trackId', …)
my_audiobooks_collection/server/index.js          géén /api/mp3
```

`my_audiobooks_collection` is de server die draait. Daar staat de mp3-route niet
in. En de site gebruikt hem ook niet: `player.js:61` zet
`audio.src = /api/stream/${t.id}`. Er is dus nooit een mp3-route geweest op de
draaiende server, en de app vroag ernaar alsof die er was.

Hoe het eruit zag: `404 NotFound … text/html … Cannot GET /api/mp3/49424`. Een
404 zonder JSON, dus de app concludeerde "de server kent de route niet" en zei
dat. Dat was waar, en het was nutteloos — er stond een weg open die de site
gewoon gebruikt.

De regel is nu:

1. vraag `/api/mp3/<spoor>` — daar staat de voortgang in (409) en daar komt de
   gerenderde mp3 vandaan als de route bestaat;
2. krijg je een 404 zonder JSON, vraag dan `/api/stream/<spoor>`, precies zoals
   de site. Dat is de weg die vaststaat te werken;
3. lees de `Content-Type` van het antwoord voordat er iets gedecodeerd wordt. Is
   het `audio/mpeg`, dan speelt het. Is het iets anders, dan zegt de app wat er
   binnenkwam en dat je het in de webinterface kunt omzetten — niet "het werkt
   niet", want dat is een klacht waar niemand iets mee kan;
4. pas als ook `/api/stream` niets geeft, zegt de app wat beide wegen zeiden.

Stap 3 is de reden dat `RangeStream` de `Content-Type` doorgeeft. Zonder die
header weet de speler pas dat het misgaat als de decoder er al aan vastzit, en dan
is het onderscheid tussen "kapot bestand" en "geen mp3" verdwenen.

Wat hiermee *niet* verandert: de beslissing "alleen mp3". `/api/stream` stuurt
het bestand zoals het op de schijf ligt, dus voor een boek dat nog niet is
omgezet komt er m4a of ogg binnen en zegt de app dat. Dat is de consequentie van
één decoder, en hij is nu zichtbaar in plaats van stil.

De andere helft van het probleem was de duur. `scan.js:195` schrijft
`f.duration || 0` in de database, en `music-metadata` geeft voor een mp3 zonder
Xing-kop geen duur. Dat is geen zeldzaamheid — dat is hier de reden dat élk deel
als `0:00` stond. Een duur van 0 betekent "onbekend", niet "leeg", en de app
behandelde het als "leeg": `PartLength` was 0, `Position` gaf 0 terug, en
`SeekInPart` begrensde alles naar 0. Dus hervatten werkte — naar het begin — en
dat leest als "hervatten doet niets". `PartLength` neemt de lengte nu van de
lezer als de server die niet heeft, en de balk zegt een streepje zolang hij die
nog niet heeft, zoals `player.js:144` (`isFinite(total) ? clock(total) : '—'`).

Nog een verschil met de site, in dezelfde lijn van de spelerbalk. Het teken van de
geluidsknop volgde de stand niet: er stond altijd "🔊", ook als het geluid uit
stond. `player.js:179` doet het wél
(`audio.muted || !audio.volume ? '🔇' : '🔊'`). Dat is het enige teken van de
geluidsregeling, dus het stond daar tijdens de proefjes terwijl er bewust niets
te horen viel. Het teken volgt de stand nu, en de naam van de knop ook — de site
heeft daar alleen de muisaanwijzer, en voor een schermlezer en een proefje is een
knop zonder toestand een knop waar je niets aan hebt.

En de sprong: `Mp3FileReaderBase` weigert `CurrentTime` zolang er nog geen frame
binnen is, en dat is de eerste milliseconde na het openen. `player.js:62` heeft
hetzelfde probleem en lost het op dezelfde manier op: `onloadedmetadata`. De app
doet dat ook — niet in de aanroep, maar in de klok, waar de wachtende sprong
blijft staan tot hij lukt.

En nog één ding, over stap 4 van de lijst hierboven. Toen de app de eerste
route als 404 binnenkreeg en de server `Cannot GET /api/mp3/49424` teruggaf,
stond er op het scherm: *de server zei niets*. Dat was onjuist, en het was een
gevolg van `Collection.OneLine`, dat van een HTML-lichaam de tekst tussen `<` en
`>` weggooide. Bij express staat de hele tekst van een 404 ín merkteken:

```html
<title>Error</title>
<pre>Cannot GET /api/mp3/49424</pre>
```

`pre` is een merkteken, dus de enige regel die iets zei ging erin mee. De
bijlage `noMp3RouteNothingSaid` was dus niet theoretisch: hij sloeg aan op een
lege regel die door deze functie was gemaakt. `OneLine` rekent nu op tags in
plaats van op merktekens: alleen `<head>`, `<style>` en `<script>` en
`<!-- … -->` worden overgeslagen. Een kale tekst zonder merkteken (de 500 van
een proxy) blijft daarmee gewoon staan, en dat is ook wat je dan wilt lezen.

De les is niet "voeg nog een voorwaarde toe" maar: een functie die de reden
moet bewaren mag niet dezelfde regels volgen als een functie die de vorm wil
opruimen. Deze volgde de vorm op en verloor de reden.

## De derde oorzaak: twee boekhoudingen (1 oktober 2026)

Boven staan twee oorzaken voor "hervatten doet niets": de mp3-route die er niet
is, en de duur van 0. Beide zaten in het afspelen zelf. Er was een derde, en die
zat nergens in het afspelen.

De app bewaarde de plek in een boek op twee plekken: bij de server, en in
`state.json` (het veld `place`). Lezen deed hij zo:

```csharp
// Player.Load, vóór
if (sameBook && from <= 0 && _store.Place is { } kept
    && kept.BookId == book.Id && kept.TrackIdx == part && kept.Position > 0)
{
    from = kept.Position;
}
```

En als de plank "Verder luisteren" leeg was, zette `RememberedBook()` daar een
boek neer dat de app zelf onthouden had, met eronder dat de plek alleen hier
stond.

Dat leek zorgvuldiger dan niets, en het was een tweede boekhouding. Waar de twee
het oneens waren — en dat gebeurde, want de app bewaart om de vijf seconden en de
server kan daar tussenuit vallen, of de sessie kan verlopen — zei de app niets.
Het ergste geval is niet dat ze het oneens waren. Het is dat de app dan een
plek toont die de server niet kent: je drukt op "Hervatten" en begint op nul. Dat
leest op het scherm als "hervatten werkt niet", en dus als hetzelfde probleem als
hierboven, terwijl het een ander probleem is met een andere oplossing.

Wat er nu staat: één bron. De voortgang komt uit het antwoord op de server
(`Book.Progress`), en wordt daarnaartoe geschreven. `Store.Place`,
`Store.BookPlace`, `SetPlace` en `RememberedBook` zijn weg, en `state.json`
schrijft geen `place` meer. De boekhouding van de app is daarmee leeg; hij
vervangt de plek niet, want er is geen tweede.

Dat kost iets, en dat mag gezegd worden. Vóór deze wijziging kon je verder
luisteren ook als het bewaren naar de server even niet lukte. Dat werkt nu niet
meer: is de server weg, dan staat de voortgang nergens. Daar staat dan ook iets
over — `savePlaceFailed` zegt nu dat je voortgang nergens staat omdat de server
hem niet bewaard heeft, en niet "hij staat in dit programma", want dat is niet
waar.

Het meten: `proefjes/voortgang-van-server.ps1` zet voortgang op de server vóórdat
de app gestart is, en kijkt dan of de app "Resume" zegt, op de juiste plek
begint, zijn voortgang naar `/api/progress` stuurt, en géén `place` in
`state.json` achterlaat. Omdat de voortgang al vaststond vóór de app er was, kan
de app er niet langs: hij gebruikt de server, of hij begint nergens.

Dat proefje is ook negatief getest, en dat leverde twee dingen op die de hele
opzet ondermijnen en dus hier thuishoren. De sabotage compileerde eerst niet
(`from` bestaat niet meer), zodat publish mislukte, het proefje tegen de oude
binary draaide en "groen" zei. En na het terugzetten van de bronnen hield
`Copy-Item` de oude tijdstempels, waardoor MSBuild de gesaboteerde dll opnieuw
verpakte: schone bronnen, verkeerde exe, en dus een oordeel over de vorige build.

De algemene les staat in `proefjes/LEES-MIJ.md`: een proefje dat groen zegt
omdat er niets gebouwd is, is het gevaarlijkste dat er is.
