# Hervatten, op een boek waarvan de server de duur niet kent.
#
# Dit is de klacht waar het allemaal om begon: "hervatten doet niets". Er zaten
# drie oorzaken aan vast, en dit proef meet ze alle drie tegelijk.
#
# Boek 6 heeft twee delen van een minuut en `duration: 0` in de database. Die 0
# is geen leeg boek maar een onbekende: `scan.js` schrijft 0 als de mp3 geen
# Xing-kop heeft, en dat doet een groot deel van een echte verzameling. In de
# app betekende die 0 toen: de klok op 0:00, elke sprong naar 0, en elke
# hervatplek naar 0. Dus "hervatten" werkte — naar het begin — en dat leest op
# het scherm als "hervatten werkt niet".
#
# Vier dingen moeten waar zijn:
#   1. geen enkel deel toont 0:00 (de site zet het etiket alleen als de server
#      een duur gaf);
#   2. na een paar seconden spelen loopt de tijd op, en de balk zegt een echte
#      duur of een streepje, maar geen leugen;
#   3. "terug" springt terug in de tijd in plaats van naar het begin;
#   4. na sluiten en heropenen begint het op de plek waar het gebleven was.
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

function Duur($id) {
  $regel = & $uia -ProcessId $id -Action text -Id SpelerDuur 2>$null
  if ($regel -notmatch "'([^']*)'") { return '' }
  return $Matches[1]
}

Say 'proefserver aanzetten'
& "$probe\proefserver-aanzetten.ps1" | Out-Null
if (-not (VraagDeServerOmGeluid)) { '   MISLUKT: de proefserver geeft geen geluid; stoppen.'; exit 1 }

Stop-Process -Name MyAudiobooks -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 700
Remove-Item "$data\fouten.log" -ErrorAction SilentlyContinue
Remove-Item "$data\state.json" -Force -ErrorAction SilentlyContinue
Remove-Item "$data\cookie.bin" -Force -ErrorAction SilentlyContinue

Say 'app starten en aanmelden'
$p = StartEnMeldAan
if ($null -eq $p) { exit 1 }


Say '1. het boek zonder duur openen'
& $open -ProcessId $p.Id -Title 'Het boek zonder duur' 2>$null | Select-Object -Last 1 | ForEach-Object { "   $_" }
Start-Sleep -Seconds 2
# De site zet het etiket met de duur alleen als de server er een gaf, en dit
# boek heeft er geen. Dus: geen enkele notitie, en zeker geen "0:00".
$noten = @(& $uia -ProcessId $p.Id -Action textsById -Id DeelNoot 2>$null | Select-Object -Skip 1)
$noten | ForEach-Object { "   $_" }
if ($noten.Count -gt 0) {
  "   MISLUKT: er staan $($noten.Count) notities bij een boek zonder duur."
} else { '   goed: geen enkele notitie, want de server kent de duur niet.' }
if ((Texts $p.Id) -match '0:00') {
  '   MISLUKT: er staat 0:00 op het scherm, en dat is een leugen over een boek van een minuut.'
} else { '   goed: nergens staat 0:00.' }

Say '2. afspelen en de klok volgen'
$n = LogCount
& $uia -ProcessId $p.Id -Action invoke -Id BoekAfspelen 2>$null | Out-Null
Start-Sleep -Seconds 4
$eerste = Tijd $p.Id
Start-Sleep -Seconds 8
$laatste = Tijd $p.Id
"   de klok na 4 s: $eerste   en na 12 s: $laatste   seconden"
"   de balk zegt: '$(Duur $p.Id)'"
if ($eerste -lt 0 -or $laatste -lt 0) {
  '   MISLUKT: de klok is niet te lezen, dus er is niets gemeten.'
} elseif ($laatste -le $eerste) {
  "   MISLUKT: de tijd loopt niet mee ($eerste -> $laatste), dus de server hoeft de duur niet te kennen."
} else {
  '   goed: de tijd loopt mee terwijl de server de duur niet kent.'
}
$duur = Duur $p.Id
if ($duur -eq '0:00') { '   MISLUKT: de balk zegt 0:00 als duur.' }
elseif ($duur -eq '') { '   MISLUKT: de balk zegt niets voor de duur.' }
elseif ($duur -notmatch '^\d+:\d\d$') { "   MISLUKT: de balk zegt '$duur' en dat is geen tijd." }
else { "   goed: de duur in de balk is '$duur', en de server gaf die niet." }

Say '3. en de notitie bij het deel dat je luistert zegt geen 0:00'
$noten = @(& $uia -ProcessId $p.Id -Action textsById -Id DeelNoot 2>$null | Select-Object -Skip 1)
$noten | ForEach-Object { "   $_" }
if (@($noten | Where-Object { $_ -match '0:00' }).Count -gt 0) {
  '   MISLUKT: er staat 0:00 bij een deel.'
} elseif ($noten.Count -lt 1) {
  '   LET OP: er is geen notitie; die is er alleen als er iets te zeggen valt.'
} else { '   goed: de notitie zegt waar je was, en geen 0:00.' }

Say '4. terug springt terug, en niet naar het begin'
# Eerst ver genoeg doorlopen. Op 12 seconden zou "terug" netjes naar 0 mogen
# springen — dat is wat de knop hoort te doen — en dan meet dit proefje niets.
while ((Tijd $p.Id) -ge 0 -and (Tijd $p.Id) -lt 30) { Start-Sleep -Seconds 2 }
$voor = Tijd $p.Id
& $uia -ProcessId $p.Id -Action invoke -Id SpelerTerug 2>$null | Out-Null
Start-Sleep -Seconds 1
$terug = Tijd $p.Id
"   van $voor seconden met 'terug' naar $terug seconden"
if ($voor -lt 20) { "   LET OP: er was maar $voor seconden om terug te springen." }
elseif ($terug -ge $voor - 5) {
  "   MISLUKT: terug zette $voor niet ver terug (kwam op $terug)."
} else { "   goed: terug zette $voor -> $terug seconden." }
Start-Sleep -Seconds 5
$naTerug = Tijd $p.Id
"   en vijf seconden later: $naTerug"
if ($naTerug -le $terug) { '   MISLUKT: na terug speelt hij niet verder.' }
else { '   goed: hij speelt door vanaf de nieuwe plek.' }

Say '5. pauzeren en de plek laten staan'
& $uia -ProcessId $p.Id -Action invoke -Id SpelerAfspelen 2>$null | Out-Null
Start-Sleep -Seconds 3
$waar = Tijd $p.Id
"   blijven staan op: $waar seconden"
Stop-Process -Name MyAudiobooks -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
$bewaard = @(LogSince $n | Where-Object { $_ -match '/api/progress' })
$bewaard | Select-Object -Last 2 | ForEach-Object { "   $_" }
if ($bewaard.Count -lt 1) { '   MISLUKT: er is geen plek bewaard op de server.' }
elseif ($waar -lt 5) { "   LET OP: er is een plek bewaard, maar de klok stond op $waar seconden." }
else { "   goed: de plek ($waar seconden) staat bij de server." }

Say '6. heropenen, en kijken waar het begint'
# De proefserver draait nog, dus de sessie van stap 1 zou nog geldig moeten zijn.
# Dat is geen aanname die mag blijven hangen: als de sessie weg is, staat de app
# op het aanmeldingsscherm en is er geen boek om te openen, en dan zou stap 6
# "goed" zeggen over een boek dat nooit is opengedaan. Dus: hij meldt zich zo
# nodig aan, en zegt welke van de twee het was.
$p = StartDeApp
Start-Sleep -Seconds 14
if ((Texts $p.Id) -match 'Sign in to carry on') {
  '   de sessie was weg, dus dit proefje meldt zich opnieuw aan'
  & $uia -ProcessId $p.Id -Action type -Id GateName -Value 'frank' 2>$null | Out-Null
  & $uia -ProcessId $p.Id -Action type -Id GatePass -Value 'probe' 2>$null | Out-Null
  & $uia -ProcessId $p.Id -Action invoke -Id GateGo 2>$null | Out-Null
  Start-Sleep -Seconds 6
  if ((Texts $p.Id) -match 'Sign in to carry on') { '   MISLUKT: aanmelden lukt niet, dus stap 6 meet niets.'; exit 1 }
} else {
  '   goed: de sessie was nog geldig, en de app ging meteen door'
}
ControleerGeluidUit | Out-Null
& $open -ProcessId $p.Id -Title 'Het boek zonder duur' 2>$null | Out-Null
Start-Sleep -Seconds 2
& $uia -ProcessId $p.Id -Action invoke -Id BoekAfspelen 2>$null | Out-Null
Start-Sleep -Seconds 4
$hervat = Tijd $p.Id
"   begonnen op: $hervat seconden"
if ($hervat -lt 0) { '   MISLUKT: de klok is niet te lezen.' }
elseif ($hervat -lt 3) { "   MISLUKT: begonnen op $hervat seconden, dus op 0: hervatten werkt niet." }
else { "   goed: begonnen op ${hervat}s, niet op 0." }

Say 'en de knop heet Resume, niet Play'
(& $uia -ProcessId $p.Id -Action buttons 2>$null | Where-Object { $_ -match 'Resume|Play' }) |
  Select-Object -First 4 | ForEach-Object { "   $_" }

Say 'fouten'
Foutenlijst

Stop-Process -Name MyAudiobooks -Force -ErrorAction SilentlyContinue
Stop-Process -Name node -Force -ErrorAction SilentlyContinue
