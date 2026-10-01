# De server kent /api/mp3 niet, en er speelt toch geluid.
#
# Dit is de standaard geworden, want zo is de draaiende server: daar staat geen
# mp3-route, want die route is alleen toegevoegd aan de andere kopie van de
# server. `player.js:61` zet `audio.src = /api/stream/${t.id}`, en dat is de
# enige geluidsroute die er is.
#
# De app vroeg eerst /api/mp3, kreeg een 404 met HTML, en zei "de collectieserver
# antwoordde niet zoals een mp3-server hoort" — wat waar was, maar waar niemand
# iets mee kon. Het punt van dit proefje is dat de app nu de weg van de site
# neemt en daadwerkelijk speelt, en dat er geen klacht op het scherm staat.
#
# Drie dingen moeten waar zijn:
#   1. de app vraagt /api/stream en krijgt 200 met audio/mpeg;
#   2. er speelt geluid en de voortgang loopt door;
#   3. er staat geen klacht over mp3 op het scherm, want er is niets mis.
$ErrorActionPreference = 'Continue'
$probe = $PSScriptRoot
$uia = "$probe\uia.ps1"
$open = "$probe\open.ps1"
$log = "$PSScriptRoot\requests.log"
. "$probe\hulp.ps1"
# De map van de app. `hulp.ps1` zet MABC_DATA, en dat is bewust een andere map
# dan die van de echte installatie: de proefjes melden zich aan op hun eigen
# proefserver, en zonder dit zouden ze de cookie en de voortgang van de
# gebruiker overschrijven. Die zou dan na elke proefronde opnieuw moeten
# aanmelden, zonder te weten waarom. Zie ook de kop van hulp.ps1.
$data = $env:MABC_DATA
$exe = "$PSScriptRoot\..\uit\MyAudiobooks.exe"


Say 'proefserver aanzetten, standaard: geen mp3-route'
& "$probe\proefserver-aanzetten.ps1" | Out-Null

Say 'de server vragen wat hij heeft'
if (-not (VraagDeServerOmGeluid)) { '   MISLUKT: geen geluid beschikbaar; stoppen.'; exit 1 }
$mp3 = curl.exe -s -o NUL -w '%{http_code}' 'http://127.0.0.1:8532/api/mp3/11' 2>$null
"   /api/mp3/11 geeft HTTP $mp3 — dat hoort hier 404 te zijn, anders meet dit proefje niets"
if ($mp3 -ne '404') { '   MISLUKT: de proefserver heeft wél /api/mp3; start hem zonder die schakelaar.'; exit 1 }

Stop-Process -Name MyAudiobooks -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 700
Remove-Item "$data\fouten.log" -ErrorAction SilentlyContinue
Remove-Item "$data\state.json" -Force -ErrorAction SilentlyContinue
Remove-Item "$data\cookie.bin" -Force -ErrorAction SilentlyContinue

Say 'app starten, aanmelden, boek openen en spelen'
$p = StartEnMeldAan
if ($null -eq $p) { exit 1 }


& $open -ProcessId $p.Id -Title 'Het eerste boek' 2>$null | Select-Object -Last 1 | ForEach-Object { "   $_" }
Start-Sleep -Seconds 2

$n = LogCount
& $uia -ProcessId $p.Id -Action invoke -Id BoekAfspelen 2>$null | Out-Null
Start-Sleep -Seconds 12
$regels = LogSince $n
$regels | Where-Object { $_ -match 'mp3|stream' } | ForEach-Object { "   $_" }

Say 'de weg die de app nam'
$mp3Vragen = ($regels | Select-String '/api/mp3').Count
$streamVragen = ($regels | Select-String '/api/stream').Count
$goedStream = ($regels | Select-String '/api/stream/\d+ 200 audio/mpeg').Count
"   gevraagd op /api/mp3: $mp3Vragen   op /api/stream: $streamVragen   daarvan 200 mp3: $goedStream"
if ($streamVragen -lt 1) { '   MISLUKT: de app vroeg niet om /api/stream, dus er is niets afgespeeld.' }
if ($goedStream -lt 1) { '   MISLUKT: het geluid is niet binnengekomen.' }

Say 'en er speelt echt iets'
$voortgang = ($regels | Select-String '/api/progress').Count
"   voortgang bewaard: $voortgang keer"
if ($voortgang -lt 1) { '   MISLUKT: geen voortgang bewaard, dus de tijd loopt niet mee.' }
else { '   goed: er is geluid en de tijd loopt mee.' }

Say 'en er staat geen klacht over mp3'
$t = (Texts $p.Id) -join '|'
if ($t -match 'did not answer as an mp3|cannot make mp3') {
  '   MISLUKT: er staat een klacht over mp3 terwijl er niets aan de hand is.'
} else { '   goed: geen klacht, want er speelt.' }

Say 'en het mp3-verzoek met zijn 404 staat in verzoeken.log'
if (Test-Path "$data\verzoeken.log") {
  $inLog = @(Get-Content "$data\verzoeken.log" | Where-Object { $_ -match '/api/mp3' })
  if ($inLog.Count -gt 0) {
    "   gevonden: $($inLog.Count) regel(s)"
    $inLog | Select-Object -Last 1 | ForEach-Object { "      $_" }
  } else { '   NIET GEVONDEN: het verzoek dat 404 kreeg staat niet in het log.' }
} else { '   (geen verzoeken.log)' }

Say 'fouten'
Foutenlijst

Stop-Process -Name MyAudiobooks -Force -ErrorAction SilentlyContinue
Stop-Process -Name node -Force -ErrorAction SilentlyContinue
