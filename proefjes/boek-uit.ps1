# Boek 2 is één deel van 45 seconden. Daarmee is de hele route te zien in één
# run: beginnen met afspelen, halverwege op de thuispagina staan met het boek
# onder "Verder luisteren", en aan het einde de melding dat het boek af is.
#
# Er wordt niet opnieuw aangemeld, dus de cookie uit de vorige run blijft
# staan; alleen de voortgang is leeg, want dit boek is nog niet beluisterd.
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

Stop-Process -Name MyAudiobooks -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 700
Remove-Item "$data\fouten.log" -ErrorAction SilentlyContinue

# Dit proefje wacht veertien keer twee seconden op het woord "klaar". Zonder
# geluid van de server komt dat woord nooit, en dan staat er na afloop alleen
# een lege regel — een proefje dat zijn eigen uitkomst niet afleest. Dus hij
# zet zijn eigen proefserver op en vraagt ernaar; zie `hulp.ps1` voor waarom.
Say 'de proefserver opzetten en vragen of hij geluid geeft'
if (-not (StartProefserver)) { '   MISLUKT: geen geluid, dus dit proefje meet niets.' ; exit 1 }

Say 'app starten en aanmelden'
$p = StartEnMeldAan
if ($null -eq $p) { exit 1 }
"processen met die naam: $((Get-Process -Name MyAudiobooks -ErrorAction SilentlyContinue | Measure-Object).Count)"

Say 'boek 2 openen en afspelen'
& $open -ProcessId $p.Id -Title 'Het tweede boek' 2>$null | Select-Object -Last 1
Start-Sleep -Seconds 3
'   de knoppen op de boekpagina:'
& $uia -ProcessId $p.Id -Action buttons 2>$null | Select-Object -Skip 1 | ForEach-Object { "      $_" }
$n = LogCount
& $uia -ProcessId $p.Id -Action invoke -Id BoekAfspelen 2>$null
Start-Sleep -Seconds 10
'   de spelerbalk:'
Texts $p.Id | Where-Object { $_ -match '^\s+.(0:|\?)' } | Select-Object -Last 4 | ForEach-Object { "      $_" }

Say 'terug naar de thuispagina terwijl het loopt'
& $uia -ProcessId $p.Id -Action invoke -Id TerugNaarPlanken 2>$null
Start-Sleep -Seconds 3
Texts $p.Id | Select-Object -Skip 5 | ForEach-Object { "      $_" }
LogSince $n | Where-Object { $_ -match 'progress' } | ForEach-Object { "   $_" }

Say 'wachten tot het deel uit is'
$seen = ''
for ($i = 0; $i -lt 24; $i++) {
  Start-Sleep -Seconds 2
  $t = (Texts $p.Id) -join '|'
  if ($t -match 'klaar|afgelopen|Beluisterd|finished|luisterde') {
    $seen = ($t -split '\|' | Where-Object { $_ -match 'klaar|afgelopen|Beluisterd|finished' }) -join ' / '
    break
  }
}
"   iets over het eind gezien: $seen"
if (-not $seen) {
  '   MISLUKT: na 48 seconden is er niets gezien dat zegt dat het boek af is.'
  '   Dat betekent dat het nooit is uitgespeeld, en niet dat de app het stil houdt.'
}
LogSince $n | Where-Object { $_ -match 'progress|finished' } | Select-Object -Last 4 | ForEach-Object { "   $_" }

Say 'weer terug naar de thuispagina'
& $uia -ProcessId $p.Id -Action invoke -Id Brand 2>$null
Start-Sleep -Seconds 3
Texts $p.Id | Select-Object -Skip 5 | ForEach-Object { "      $_" }
LogSince $n | Where-Object { $_ -match 'progress|finished' } | Select-Object -Last 3 | ForEach-Object { "   $_" }

Say 'fouten'
if (Test-Path "$data\fouten.log") { (Get-Content "$data\fouten.log" -Raw) }
else { '   (geen foutenlijst)' }
