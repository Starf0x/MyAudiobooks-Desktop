# Boek 3: het track dat nog omgezet moet worden.
#
# De fixture geeft de eerste twee vragen een 409 met voortgang, en daarna het
# geluid. Dit script drukt op afspelen en kijkt wat er op het scherm staat
# tijdens het omzetten, en of het geluid daarna vanzelf komt.
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

function Say($text) { "=== $text" }
function LogCount { (Get-Content $log -ErrorAction SilentlyContinue | Measure-Object -Line).Lines }
function LogSince($n) { Get-Content $log | Select-Object -Skip $n }

Stop-Process -Name MyAudiobooks -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 700
Remove-Item "$data\fouten.log" -ErrorAction SilentlyContinue

# Dit is het enige proefje dat de mp3-route nodig heeft: de 409 met voortgang
# bestaat alleen daar. Dus hij start zijn eigen proefserver, mét die route, en
# zegt dat ook. Vraagt hij een bestaande server om de 409 en vindt een 404, dan
# meet hij het omzetten niet en zegt hij het — maar dan is er niets gemeten en
# geen regel in dit bestand die daarover struikelt.
Say 'proefserver aanzetten mét de mp3-route'
if (-not (StartProefserver -Mp3Route)) { '   MISLUKT: geen geluid, dus dit proefje meet niets.' ; exit 1 }
if (-not (VraagDeServerOmMp3)) { '   MISLUKT: geen mp3-route, dus dit proefje meet niets.' ; exit 1 }

Say 'starten en aanmelden'
# Niet "de aanmelding van de vorige keer blijft staan": de proefserver is zojuist
# herstart en vergeet bij elke start zijn sessies. Zonder aanmelden stond de app
# op het aanmeldingsscherm, en dit proefje meldde dan dat boek 3 niet te vinden
# was en dat er geen 409 kwam — twee klachten over het verkeerde onderwerp.
$p = StartEnMeldAan
if ($null -eq $p) { exit 1 }
"processen met die naam: $((Get-Process -Name MyAudiobooks -ErrorAction SilentlyContinue | Measure-Object).Count)"

Say 'thuispagina'
& $uia -ProcessId $p.Id -Action texts 2>$null | Select-Object -Skip 1 | ForEach-Object { "   $_" }

$n = LogCount
Say 'boek 3 openen'
& $open -ProcessId $p.Id -Title 'Het boek dat nog omgezet moet worden' 2>$null | Select-Object -Last 1
Start-Sleep -Seconds 3
LogSince $n | ForEach-Object { "   $_" }

$n = LogCount
Say 'afspelen terwijl er nog omgezet wordt'
& $uia -ProcessId $p.Id -Action invoke -Id BoekAfspelen 2>$null
Start-Sleep -Milliseconds 1200
'   scherm 1,5 s na de klik:'
& $uia -ProcessId $p.Id -Action texts 2>$null | Select-Object -Skip 1 | Where-Object { $_ -match '%|omzet|bezig|werkt|geduld' } | ForEach-Object { "      $_" }
Start-Sleep -Seconds 6
'   scherm na het omzetten:'
& $uia -ProcessId $p.Id -Action texts 2>$null | Select-Object -Skip 1 | Select-Object -Last 14 | ForEach-Object { "      $_" }
LogSince $n | ForEach-Object { "   $_" }

# Beoordeeld, en niet alleen afgedrukt. De 409 hoort er twee keer in en de 200
# één keer; zonder deze telling is een ronde waarin er niets speelde precies
# even groen als een waarin alles werkte.
$regels = LogSince $n
$bezig = ($regels | Select-String '409').Count
$klaar = ($regels | Select-String '200 klaar').Count
"   409 tijdens het omzetten: $bezig   200 en klaar: $klaar"
if ($bezig -lt 2) { "   MISLUKT: de server gaf geen 409, dus er is niets omgezet en er viel niets te wachten." }
if ($klaar -lt 1) { '   MISLUKT: het geluid is na het omzetten niet vanzelf gekomen.' }
if ($bezig -ge 2 -and $klaar -ge 1) { '   goed: de app wachtte op de 409 en begon vanzelf na de 200.' }

Say 'fouten'
if (Test-Path "$data\fouten.log") { (Get-Content "$data\fouten.log" -Raw).Substring(0, [Math]::Min(900, (Get-Item "$data\fouten.log").Length)) }
else { '   (geen foutenlijst)' }

# Net als bij de andere proefjes: een schakelaar die in de omgeving blijft
# staan maakt de volgende proef stiekem onvolledig. Deze staat er nu vaak.
Remove-Item Env:\APP_PROBE_MP3 -ErrorAction SilentlyContinue
