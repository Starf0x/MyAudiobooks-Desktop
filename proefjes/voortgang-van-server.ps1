# De voortgang van elk boek komt van de server.
#
# Dit is de derde helft van "hervatten doet niets", naast `hervatten.ps1`. Dat
# proefje meet dat het afspelen op de juiste plek begint. Dit meet iets anders,
# en iets wat gemakkelijk stukgaat zonder dat iemand het merkt: dat de app niet
# zelf een boekhouding voert.
#
# Waarom dat apart een proefje is: de app bewaarde de plek in een boek ook in
# `state.json`, naast de server. Dat leek handig — je kon verder luisteren ook
# als het bewaren naar de server even niet lukte — maar het is een tweede
# waarheid, en die loopt uit de eerste. Waar de twee het oneens waren, zei de
# app niets. En het ergste geval is niet dat ze het oneens waren: het is dat de
# app dan een plek toont die de server niet kent, zodat je op "Hervatten" drukt
# en op nul begint. Dat is precies de klacht waar dit programma om begonnen is,
# en de oorzaak was twee keer iets anders dan de oorzaak die ernaast lag.
#
# Dus twee dingen, en allebei gemeten in plaats van aangenomen:
#
#   1. de server krijgt de voortgang te zien, en de app toont daarna wat de
#      server heeft — niet iets wat er lokaal staat;
#   2. `state.json` bevat geen voortgang meer, want er is niets om te bewaren.
#
# Stap 1 gebruikt `/api/progress` rechtstreeks, vóór de app eraan komt. Zo
# staat de voortgang vast in de database van de proefserver, en kan de app er
# niet langs: hij moet dezelfde bron gebruiken of nergens beginnen.
$ErrorActionPreference = 'Continue'
$probe = $PSScriptRoot
$uia = "$probe\uia.ps1"
$open = "$probe\open.ps1"
$log = "$PSScriptRoot\requests.log"
. "$probe\hulp.ps1"
# De map van de app. `hulp.ps1` zet MABC_DATA, en dat is bewust een andere map
# dan die van de echte installatie: de proefjes melden zich aan op hun eigen
# proefserver, en zonder dit zouden ze de cookie van de gebruiker overschrijven.
# Zie ook de kop van hulp.ps1.
$data = $env:MABC_DATA
$exe = "$PSScriptRoot\..\uit\MyAudiobooks.exe"

# De klok van de speler, in seconden. -1 betekent "onleesbaar", en dat is een
# antwoord op zich: een proefje dat -1 ziet en "goed" zegt, meet niets.
function Tijd($id) {
  $regel = & $uia -ProcessId $id -Action text -Id SpelerTijd 2>$null
  if ($regel -notmatch "'([^']*)'") { return -1 }
  $stukken = $Matches[1].Split(':')
  if ($stukken.Count -lt 2) { return -1 }
  $n = 0
  foreach ($s in $stukken) {
    if ($s -notmatch '^\d+$') { return -1 }
    $n = $n * 60 + [int]$s
  }
  return $n
}

# Zet voortgang voor een boek bij gebruiker `frank`, rechtstreeks op de server.
#
# Eerst aanmelden, zodat er een sessie is, en die meesturen. Zo gaat het in de
# app ook, alleen met HttpClient in plaats van met `Invoke-RestMethod`.
#
# `Invoke-RestMethod` en niet `curl.exe -d`, en dat is een maat voor de
# waarheid en niet voor de gemak. Windows leest de aanhalingstekens uit een
# commandoregel als argumenttekens weg, dus `curl.exe` stuurde `{name:frank,...}`
# in plaats van `{"name":"frank",...}`. De server antwoordde toen met een
# SyntaxError over "Expected property name or '}' in JSON at position 1" — en
# dat is een proefje dat zijn eigen opzet niet kan uitleggen en dus niets over de
# app zegt. Twee pogingen met escaped quotes in plaats van een here-string
# faalden op dezelfde manier. `Invoke-RestMethod` geeft de string door zoals hij
# is.
function ZetVoortgangOpDeServer($boek, $deel, $seconde) {
  $sessie = New-Object Microsoft.PowerShell.Commands.WebRequestSession
  $null = Invoke-RestMethod -Method Post `
    -Uri 'http://127.0.0.1:8532/api/account/signin' `
    -ContentType 'application/json' `
    -Body (@{ name = 'frank'; password = 'probe' } | ConvertTo-Json) `
    -WebSession $sessie
  $antwoord = Invoke-RestMethod -Method Post `
    -Uri 'http://127.0.0.1:8532/api/progress' `
    -ContentType 'application/json' `
    -Body (@{ user = 'frank'; bookId = $boek; trackIdx = $deel; position = $seconde } | ConvertTo-Json) `
    -WebSession $sessie
  return ($antwoord | ConvertTo-Json -Compress)
}

Say 'proefserver aanzetten'
if (-not (StartProefserver)) { '   MISLUKT: de proefserver geeft geen geluid; stoppen.'; exit 1 }

Stop-Process -Name MyAudiobooks -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 700
Remove-Item "$data\fouten.log" -ErrorAction SilentlyContinue
Remove-Item "$data\state.json" -Force -ErrorAction SilentlyContinue
Remove-Item "$data\cookie.bin" -Force -ErrorAction SilentlyContinue

Say '1. voortgang op de server zetten, vóórdat de app start'
# Boek 6 is het boek zonder duur, en dat is het boek waar `hervatten` ook mee
# werkt. 37 seconden is ver genoeg om boven nul uit te komen en laag genoeg om
# niet aan het eind van een deel te zitten.
$antwoord = ZetVoortgangOpDeServer 6 0 37
"   de server zei: $antwoord"
# `/api/progress` antwoordt `{ ok: true, done: …, cleared: … }`. Naar `ok` kijken
# en niet naar de vorm: een proefje dat op de precieze tekst van een antwoord
# let, breekt zodra de server iets netter teruggeeft, en dan zegt het "niets
# gevonden" terwijl er iets gevonden is.
if ($antwoord -notmatch '"ok"\s*:\s*true') {
  '   MISLUKT: de server nam de voortgang niet aan, dus dit proefje meet niets.'
  exit 1
}

Say '2. app starten en aanmelden, en state.json bekijken vóór het afspelen'
$p = StartEnMeldAan
if ($null -eq $p) { exit 1 }

$staat = Get-Content "$data\state.json" -Raw
"   de regels in state.json:"
($staat -split "`n") | ForEach-Object { "      $($_)" }
if ($staat -match '"place"') {
  '   MISLUKT: state.json heeft een "place", en dat is voortgang die niet bij de'
  '   server hoort. Zie Store.Write in Store.cs.'
} else {
  '   goed: er staat nog geen voortgang in state.json.'
  '   Let op: dat is hier nog geen bewijs. Er is nu nog niets afgespeeld, dus er'
  '   valt ook nog niets te bewaren. De echte controle is stap 6, ná het spelen.'
}

Say '3. het boek openen: moet Hervatten zeggen, en op 37 seconden beginnen'
$n = LogCount
& $open -ProcessId $p.Id -Title 'Het boek zonder duur' 2>$null | Select-Object -Last 1 | ForEach-Object { "   $_" }
Start-Sleep -Seconds 3

$knop = @(& $uia -ProcessId $p.Id -Action buttons 2>$null | Where-Object { $_ -match 'BoekAfspelen' })
$knop | ForEach-Object { "   $_" }
if (@($knop | Where-Object { $_ -match 'Resume' }).Count -eq 0) {
  '   MISLUKT: de knop zegt niet Resume. De app weet dus niet dat er voortgang is,'
  '   terwijl de server die voortgang wél heeft.'
} else { '   goed: de knop zegt Resume, en dat komt van de server.' }

Say '4. afspelen, en kijken waar het begint'
& $uia -ProcessId $p.Id -Action invoke -Id BoekAfspelen 2>$null | Out-Null
Start-Sleep -Seconds 5
$waar = Tijd $p.Id
"   begonnen op: $waar seconden"
if ($waar -lt 0) { '   MISLUKT: de klok is niet te lezen, dus er is niets gemeten.' }
elseif ($waar -lt 30) {
  "   MISLUKT: begonnen op $waar seconden. De server had er 37 staan."
} else { "   goed: begonnen op ${waar}s, en dat is de 37 van de server." }

Say 'en nu er is een boek open: zegt het scherm zelf dat het geluid uit staat'
# Nu pas staat de spelerbalk op het scherm, en daarmee de geluidsknop. Dus dit kan
# niet in stap 2: toen was er nog geen boek open en dus geen balk. Zie
# `ControleerDempenOpHetScherm` in hulp.ps1.
ControleerDempenOpHetScherm $p.Id | Out-Null

Say '5. de app stuurt zijn eigen voortgang, en niet iets uit een eigen bestand'
$verzoeken = LogSince $n | Where-Object { $_ -match '/api/progress' }
$verzoeken | Select-Object -Last 3 | ForEach-Object { "   $_" }
if (@($verzoeken).Count -lt 1) { '   MISLUKT: de app stuurde geen voortgang, dus hij houdt die nergens vast.' }
else { '   goed: de app bewaart zijn voortgang bij de server.' }

Say '6. en nu is er wél voortgang, dus hier is te zien waar die terechtkomt'
# Dit is de stap die het verschil maakt. In stap 2 was er nog niets afgespeeld, en
# toen had die regel er ook mogen zijn zonder één klacht. Gecontroleerd is dat:
# de app met de tweede boekhouding weer in de build zetten geeft hier een
# MISLUKT en verderop overal "goed".
#
# Er is dus nu iets te bewaren, en er staat niets op schijf: het is bij de server
# gebleven, want stap 5 liet zien dat de app het daar naartoe stuurde.
$staat = Get-Content "$data\state.json" -Raw
if ($staat -match '"place"') {
  '   MISLUKT: er is een "place" in state.json ontstaan. De app houdt dus een'
  '   tweede voortgang bij naast die van de server, en waar de twee het oneens'
  '   waren zei niemand iets.'
} else {
  '   goed: er is voortgang, en die staat niet op schijf maar bij de server.'
}

Say 'fouten'
Foutenlijst

Stop-Process -Name MyAudiobooks -Force -ErrorAction SilentlyContinue
Stop-Process -Name node -Force -ErrorAction SilentlyContinue
