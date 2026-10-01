# Een deel dat geen mp3 is, en de klacht die daarbij hoort.
#
# Boek 5 ligt als m4a op de schijf. De webinterface speelt het zonder moeite —
# Chromium leest aac — en deze app niet: er is één decoder en die spreekt geen
# aac. Dat is een bewuste keuze (ONDERZOEK-AUDIO.md), en een bewuste keuze die
# je niet hoort zitten is geen keuze.
#
# Wat de app dus moet zeggen: wat er binnenkwam ("audio/mp4"), en wat er mee te
# doen is (omzetten in de webinterface). Niet "het kon niet spelen", want dat is
# een klacht waar niemand iets mee kan. En zeker niet "0:00", want dan lijkt het
# alsof er niets te horen valt.
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


Say 'proefserver aanzetten en de m4a vragen'
& "$probe\proefserver-aanzetten.ps1" | Out-Null
if (-not (VraagDeServerOmGeluid)) { '   MISLUKT: de proefserver geeft geen geluid; stoppen.'; exit 1 }
"   /api/stream/51 zegt: $((curl.exe -s -o NUL -w '%{content_type}' -r 0-0 'http://127.0.0.1:8532/api/stream/51'))"

Stop-Process -Name MyAudiobooks -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 700
Remove-Item "$data\fouten.log" -ErrorAction SilentlyContinue
Remove-Item "$data\state.json" -Force -ErrorAction SilentlyContinue
Remove-Item "$data\cookie.bin" -Force -ErrorAction SilentlyContinue

Say 'app starten, aanmelden, het m4a-boek openen en spelen'
$p = StartEnMeldAan
if ($null -eq $p) { exit 1 }


& $open -ProcessId $p.Id -Title 'Het boek dat nog geen mp3 is' 2>$null | Select-Object -Last 1 | ForEach-Object { "   $_" }
Start-Sleep -Seconds 2
$n = LogCount
& $uia -ProcessId $p.Id -Action invoke -Id BoekAfspelen 2>$null | Out-Null

Say 'wat de app zegt'
# Wachten op de melding in plaats van een vaste tijd slapen: de app houdt haar
# zeven seconden, en dit proefje sliep daar zes. Dat is een proefje dat bij
#naaste doorvalt in plaats van te slagen.
$wacht = WachtOpTekst $p.Id 'mp3|Convert|omzetten|audio/mp4'
$regels = $wacht | ForEach-Object { "   $_" }
$regels
$t = (Texts $p.Id) -join '|'

if ($t -match 'not mp3' -and $t -match 'audio/mp4') {
  '   goed: gezegd dat het geen mp3 is, en wat de server stuurde.'
} else {
  '   MISLUKT: de app zegt niet dat dit geen mp3 is, of niet wat de server stuurde.'
}
if ($t -match 'Convert to MP3' -or $t -match 'Omzetten naar mp3') {
  '   goed: en gezegd wat er mee te doen is.'
} else { '   MISLUKT: er staat niets over omzetten, dus de klacht is niet te behandelen.' }
if ($t -match 'could not|kon niet') { '   LET OP: er staat een algemene klacht bij, naast de eigen.' }

Say 'en er speelt niets, en dat is te zien'
$logregels = LogSince $n
"   verzoeken om dit spoor: $((($logregels | Select-String '/api/stream/51').Count))"
$voortgang = ($logregels | Select-String '/api/progress').Count
"   voortgang bewaard: $voortgang (moet 0 zijn: er is niets gespeeld)"
if ($voortgang -gt 0) { '   MISLUKT: er is voortgang bewaard, dus er is toch gespeeld.' }

Say 'fouten'
Foutenlijst

Stop-Process -Name MyAudiobooks -Force -ErrorAction SilentlyContinue
Stop-Process -Name node -Force -ErrorAction SilentlyContinue
