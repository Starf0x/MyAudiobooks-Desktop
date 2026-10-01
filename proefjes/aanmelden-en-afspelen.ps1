# The whole flow in one go: start, sign in, open a book, press play, and write
# down what the server saw and what the app complained about.
#
# Nobody can watch the window here, so the proof is in the fixture's request log
# and in the app's own error file. Both are read at the end, and both say what
# happened.
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
function LogSince($n) { Get-Content $log | Select-Object -Skip $n }
function LogCount { (Get-Content $log -ErrorAction SilentlyContinue | Measure-Object -Line).Lines }

Stop-Process -Name MyAudiobooks -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 600
Remove-Item "$data\fouten.log" -ErrorAction SilentlyContinue
# Beide bestanden weg, en niet alleen `state.json`. Een cookie van een andere
# server is een sessie die de proefserver niet kent, en dan lijkt het alsof
# aanmelden niet werkt.
#
# `$data` is de map van het proefje, niet die van je installatie: `hulp.ps1`
# zet MABC_DATA. Vroeger stonden die proefjes in `%LOCALAPPDATA%\My Audiobooks`
# en hebben ze daar de cookie en de voortgang van de gebruiker weggegooid, zonder
# kopie. Daarom staat hier geen pad naar een "state-backup": die zou er nog zijn
# als de veiligheid er was.
if (Test-Path $data) {
  Remove-Item "$data\state.json" -Force -ErrorAction SilentlyContinue
  Remove-Item "$data\cookie.bin" -Force -ErrorAction SilentlyContinue
}

Say 'de proefserver opzetten en vragen of hij geluid geeft'
if (-not (StartProefserver)) { '   MISLUKT: geen server, dus dit proefje meet niets.'; exit 1 }

Say 'start de app'
$p = StartDeApp
Start-Sleep -Seconds 13
"pid $($p.Id), draait: $(-not $p.HasExited)"
$procs = Get-Process -Name MyAudiobooks -ErrorAction SilentlyContinue
"processen met die naam: $(($procs | Measure-Object).Count)"

$n = LogCount
Say 'aanmelden'
& $uia -ProcessId $p.Id -Action type -Id GateName -Value 'frank' 2>$null | Out-Null
& $uia -ProcessId $p.Id -Action type -Id GatePass -Value 'probe' 2>$null | Out-Null
& $uia -ProcessId $p.Id -Action invoke -Id GateGo 2>$null
Start-Sleep -Seconds 6
ControleerGeluidUit | Out-Null
LogSince $n | ForEach-Object { "   $_" }

$n = LogCount
Say 'boek openen'
& $open -ProcessId $p.Id -Title 'Het eerste boek' 2>$null | Select-Object -Last 2
Start-Sleep -Seconds 4
LogSince $n | ForEach-Object { "   $_" }
$mp3NaOpenen = (LogSince $n | Select-String -Pattern '/api/mp3').Count
"mp3-verzoeken na het openen: $mp3NaOpenen (moet 0 zijn: er speelt niets vanzelf)"

$n = LogCount
Say 'afspelen'
# Op id, want een id verandert niet met de taal en niet met de volgorde van de
# knoppen. Zie `BoekAfspelen` in MainWindow.Book.cs.
& $uia -ProcessId $p.Id -Action invoke -Id BoekAfspelen 2>$null
Start-Sleep -Seconds 25
$verzoeken = LogSince $n
$verzoeken | ForEach-Object { "   $_" }
$mp3 = ($verzoeken | Select-String '/api/mp3').Count
$voortgang = ($verzoeken | Select-String '/api/progress').Count
"mp3: $mp3  voortgang: $voortgang"

# Hier wordt beoordeeld, en niet alleen geteld. Dit proefje typte de twee
# aantallen af en ging daarna door, en dat is precies hoe het drie ronden lang
# "mp3: 0" kon zeggen zonder dat iemand het zag: de server draaide zonder
# mp3-route, er speelde niets, en het proefje sloot af met een regel tekst en
# geen oordeel. Een proefje dat alleen vertelt wat het zag is een dagboek.
if ($mp3 -lt 1) {
  '   MISLUKT: er is geen enkel verzoek om geluid gedaan, dus er is niets afgespeeld.'
  '   Kijk of de proefserver draait (zie proefserver-aanzetten.ps1).'
}
if ($voortgang -lt 1) {
  '   MISLUKT: er is geen voortgang bewaard, dus de speler heeft de tijd niet bijgehouden.'
}
if ($mp3 -ge 1 -and $voortgang -ge 1) { '   goed: er is om geluid gevraagd en de voortgang is bewaard.' }

Say 'wat er op het scherm staat'
& $uia -ProcessId $p.Id -Action texts 2>$null | Select-Object -Skip 1 | ForEach-Object { "   $_" }

Say 'fouten'
if (Test-Path "$data\fouten.log") {
  $f = Get-Content "$data\fouten.log" -Raw
  if ($f.Length -gt 1200) { $f.Substring(0, 1200) + "`n   ... (afgekapt)" } else { $f }
} else { '   (geen foutenlijst)' }
