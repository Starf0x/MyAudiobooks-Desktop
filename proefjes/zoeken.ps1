# Zoeken werkt, en zegt wat het deed.
#
# Zoeken is de enige manier om een boek te vinden waarvan je de auteur of het
# genre niet weet, en het is een van de dingen die het langst stil hebben
# gewerkt: het veld stond er, er stond een handler op, en er gebeurde niets. Een
# proefje dat daar niets van merkt is dan ook waardeloos — dat is precies wat er
# toen gebeurde.
#
# Drie dingen moeten waar zijn:
#   1. er komt een antwoord van de server, en dat is een zoekopdracht en geen
#      genre of auteur (anders zoek je in het verkeerde);
#   2. de gevonden boeken staan er, met hun naam;
#   3. een zoekopdracht zonder uitkomst zegt dat, en zegt niks anders.
$ErrorActionPreference = 'Continue'
$probe = $PSScriptRoot
$uia = "$probe\uia.ps1"
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

Say 'proefserver opzetten'
if (-not (StartProefserver)) { '   MISLUKT: geen server, dus dit proefje meet niets.'; exit 1 }

Stop-Process -Name MyAudiobooks -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 700
Remove-Item "$data\fouten.log" -ErrorAction SilentlyContinue
Remove-Item "$data\state.json" -Force -ErrorAction SilentlyContinue
Remove-Item "$data\cookie.bin" -Force -ErrorAction SilentlyContinue

$n = LogCount
Say 'app starten en aanmelden'
$p = StartEnMeldAan
if ($null -eq $p) { exit 1 }

$n = LogCount
Say 'zoeken op "tweede"'
& $uia -ProcessId $p.Id -Action type -Id Search -Value 'tweede' 2>$null | Out-Null
# Wachten op wat er moet verschijnen, in plaats van een vaste tijd slapen en
# dan kijken of er toevallig iets staat. Een melding staat zeven seconden.
$gevonden = WachtOpTekst $p.Id 'Het tweede boek'
LogSince $n | ForEach-Object { "   $_" }
if ($gevonden.Count -gt 0) { '   gevonden: Het tweede boek staat er' }
else { '   NIET GEVONDEN: Het tweede boek ontbreekt' }

$n = LogCount
Say 'zoeken op iets dat er niet is'
& $uia -ProcessId $p.Id -Action type -Id Search -Value 'qqqqqqqqqq' 2>$null | Out-Null
$leeg = WachtOpTekst $p.Id 'qqqqqqqqqq'
LogSince $n | ForEach-Object { "   $_" }
if ($leeg.Count -gt 0) { '   gezegd wat er niet gevonden is' }
else { '   NIET GEZEGD: de zoekopdracht verdween zonder uitleg' }

Say 'fouten'
if (Test-Path "$data\fouten.log") { Get-Content "$data\fouten.log" -Raw } else { '   (geen foutenlijst)' }

Stop-Process -Name MyAudiobooks -Force -ErrorAction SilentlyContinue
Stop-Process -Name node -Force -ErrorAction SilentlyContinue
