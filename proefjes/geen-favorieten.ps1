# De laatste belofte: als de server /api/favourites niet kent, verdwijnt het
# hart en zegt de app er niets over.
#
# Een 404 op die route is geen fout maar een antwoord: de collectie heeft geen
# favorieten. De app moet dan het hart weglaten. Zeggen dat er iets mis is zou
# onzin zijn — er is immers niets mis, alleen geen functie.
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
function Texts($id) { & $uia -ProcessId $id -Action texts 2>$null | Select-Object -Skip 1 }

Say 'proefserver opzetten zonder /api/favourites'
if (-not (StartProefserver -GeenFavorieten)) { '   MISLUKT: geen server, dus dit proefje meet niets.'; exit 1 }
"antwoord op /api/favourites: $((curl.exe -s -o NUL -w '%{http_code}' http://127.0.0.1:8532/api/favourites))"

Stop-Process -Name MyAudiobooks -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 700
Remove-Item "$data\fouten.log" -ErrorAction SilentlyContinue
Remove-Item "$data\state.json" -Force -ErrorAction SilentlyContinue
Remove-Item "$data\cookie.bin" -Force -ErrorAction SilentlyContinue

$n = LogCount
Say 'app starten, aanmelden, thuispagina'
$p = StartEnMeldAan
if ($null -eq $p) { exit 1 }
LogSince $n | ForEach-Object { "   $_" }
'   thuispagina:'
Texts $p.Id | Select-Object -Skip 5 | ForEach-Object { "      $_" }

$n = LogCount
Say 'boekpagina: is er een hart?'
& $open -ProcessId $p.Id -Title 'Het eerste boek' 2>$null | Select-Object -Last 1
Start-Sleep -Seconds 3
& $uia -ProcessId $p.Id -Action buttons 2>$null | Select-Object -Skip 1 | ForEach-Object { "      $_" }
LogSince $n | ForEach-Object { "   $_" }

Say 'is er iets gezegd over favorieten?'
$t = (Texts $p.Id) -join '|'
if ($t -match 'favo|hart|heart|♥|♡') { "   JA: $((Texts $p.Id) | Where-Object { $_ -match 'favo|hart|heart|♥|♡' })" }
else { '   nee, nergens een hart of een klacht' }

Say 'fouten'
if (Test-Path "$data\fouten.log") { Get-Content "$data\fouten.log" -Raw } else { '   (geen foutenlijst)' }

# Om dezelfde reden als in `geen-mp3-route.ps1`: een schakelaar die in de
# omgeving blijft staan, maakt de volgende proef stiekem onvolledig.
Remove-Item Env:\APP_PROBE_NO_FAVS -ErrorAction SilentlyContinue

Stop-Process -Name MyAudiobooks -Force -ErrorAction SilentlyContinue
Stop-Process -Name node -Force -ErrorAction SilentlyContinue
