# Kleine gedeelde stukjes voor de proefjes.
#
# Alles wat twee of meer proefjes nodig hebben, staat hier, zodat het op één
# plek staat en niet in vier kopieën die elk een eigen bug hebben.
#
# Waar de app zijn bestanden bewaart: een eigen map, niet die van de echte
# installatie.
#
# Dit is niet netheid. Elk proefje start zijn eigen proefserver en meldt zich
# daar aan, en de app bewaart zijn cookie en zijn voortgang in LocalAppData.
# Zonder deze regel overschrijven de proefjes dus iedere ronde de aanmelding en
# de voortgang van de gebruiker, en die staat na elke proefronde op het
# aanmeldingsscherm zonder te weten waarom. Toen het nog niet zo was, was het
# ook zo: de proefjes sindsdien hebben de aanmelding van de gebruiker weggegooid
# en er is geen kopie van bewaard.
#
# `App.Choose()` leest dezelfde variabele, en zegt het ronduit als hij ontoegankelijk
# is, in plaats van stilletjes ergens anders te gaan schrijven.
$env:MABC_DATA = Join-Path $env:TEMP 'proefjes-mabc'

function Say($text) { "=== $text" }
function LogCount { (Get-Content $log -ErrorAction SilentlyContinue | Measure-Object -Line).Lines }
function LogSince($n) { Get-Content $log | Select-Object -Skip $n }
function Texts($id) { & $uia -ProcessId $id -Action texts 2>$null | Select-Object -Skip 1 }
function Labels($id) { & $uia -ProcessId $id -Action labels 2>$null }

function Foutenlijst {
  if (Test-Path "$data\fouten.log") { Get-Content "$data\fouten.log" -Raw } else { '   (geen foutenlijst)' }
}

function StopAll {
  Stop-Process -Name MyAudiobooks -Force -ErrorAction SilentlyContinue
  Stop-Process -Name node -Force -ErrorAction SilentlyContinue
}

<#
  Zet het geluid uit in de instellingen van de app, vóór het starten.

  Waarom hier en niet in de app: een proefje draait een echt boek af, en dat
  boek is hoorbaar. Een proefje dat de kamer van iemand anders vol speelt is een
  proefje waar je niet naast kunt werken, en de oplossing die hiervoor in de
  app gebouwd is — een `MABC_MUTE` — is een schakelaar die alleen bestaat voor
  proefjes, en daar staat deze code liever niet voor.

  Er is al een echte instelling: `Store.SetSound(volume, muted)`, en die staat
  in `state.json`. De app leest dat bestand bij het opstarten en zet er het
  geluid mee uit. Dus schrijft dit proefje die twee waarden, en de app doet de
  rest zelf.

  Dat het werkt is niet aangenomen maar na te lezen: `Store.Take` leest `muted`
  en `volume`, `Player` neemt ze over in zijn constructor, en `Open` zet
  `output.Volume = 0` als het geluid uit staat. Wat hier gebeurt is dus geen
  truc maar een instelling.

  Het bestand wordt geschreven met een byte-order mark, want dat is wat
  `Set-Content -Encoding UTF8` in Windows PowerShell doet. De app trekt die
  mark eraf voordat hij het leest (`Store.Load`), dus dit is meteen ook een
  proefje van dergelijke bestanden.
#>
function ZetGeluidUit {
  $map = $env:MABC_DATA
  if (-not (Test-Path $map)) { New-Item -ItemType Directory -Path $map -Force | Out-Null }
  $bestand = Join-Path $map 'state.json'
  $staat = $null
  if (Test-Path $bestand) {
    try { $staat = Get-Content $bestand -Raw | ConvertFrom-Json } catch { $staat = $null }
  }
  if ($null -eq $staat) { $staat = [pscustomobject]@{} }
  $staat | Add-Member -NotePropertyName muted -NotePropertyValue $true -Force
  $staat | Add-Member -NotePropertyName volume -NotePropertyValue 0 -Force
  $staat | ConvertTo-Json -Depth 5 | Set-Content -Path $bestand -Encoding UTF8
  Write-Host '   geluid staat uit in de instellingen van het proefje (state.json)'
}

<#
  Start de app tegen de proefserver, met het geluid uit en het venster
  geminimaliseerd, en zegt wat er is gebeurd.

  Het geminimaliseerd starten is `-WindowStyle Minimized`, en dat werkt omdat de
  app de standaard vensterstand van Windows overneemt. Het is gemeten, niet
  aangenomen: `IsIconic` is waar vanaf de eerste seconde dat het venster er is,
  en UI Automation leest en klikt daarna gewoon door (`GateName`, `GateGo` en
  de boekkaarten allemaal). Dus de proefjes zien hetzelfde scherm als anders,
  maar het scherm staat niet meer over je bureau.

  De reden dat dit er is: een proefronde duurt minuten en opent tientallen
  vensters, en dat is een reden om niets te doen terwijl er gewerkt wordt. Ook
  het geluid was een reden, en dat zit nu in `ZetGeluidUit`.
#>
function StartDeApp {
  $env:MABC_URL = 'http://127.0.0.1:8532'
  ZetGeluidUit
  $app = Start-Process -FilePath $exe -PassThru -WindowStyle Minimized
  $env:MABC_URL = $null

  $begin = [DateTime]::UtcNow
  $h = [IntPtr]::Zero
  while (([DateTime]::UtcNow - $begin).TotalSeconds -lt 40) {
    $app.Refresh()
    if ($app.MainWindowHandle -ne 0) { $h = $app.MainWindowHandle; break }
    Start-Sleep -Milliseconds 100
  }
  if ($h -eq [IntPtr]::Zero) {
    Write-Host '   LET OP: het venster is na 40 s nog niet verschenen.'
  } elseif (IsGeminimaliseerd $h) {
    Write-Host '   het venster is geminimaliseerd, dus het staat niet over je scherm'
  } else {
    Write-Host '   LET OP: het venster is niet geminimaliseerd en ligt dus op je scherm.'
    Write-Host '   Dat is nooit de bedoeling; kijk of een ander programma de'
    Write-Host '   vensterstand dwangsluit.'
  }
  return $app
}

<#
  Staat het venster op het icoontje in de taakbalk in plaats van open?

  `ShowWindowAsync` is niet aan de beurt: dit leest alleen. Er is eerder
  geprobeerd het venster ná het opstarten te minimaliseren, en dat liet zien dat
  de app er keurig op reageerde — maar dan heeft het al een seconde op je
  scherm gestaan, en een seconde is precies lang genoeg om ervan geschrokken te
  zijn. Dus dit controleert of het vanaf het begin zo is.
#>
if (-not ('ProefVenster' -as [type])) {
  Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ProefVenster {
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
}
'@
}
function IsGeminimaliseerd($h) { [ProefVenster]::IsIconic($h) }

<#
  Wacht tot er een regel op het scherm staat die aan het patroon voldoet, en geef
  die regels terug.

  Een proefje dat na een vaste tijd leest, is een proefje dat soms een scherm
  leest waar de melding al van af is. En dat is hier geen theorie: de app houdt
  een melding zeven seconden zichtbaar (`MainWindow.Say`, bewust langer dan de
  2,6 van de site, want sommige klachten moet je kunnen overtypen). `geen-geluid`
  las na zeven seconden, en vond dan meestal niets — terwijl dezelfde melding er
  twee seconden na de klik gewoon stond. Het proefje meet dan de klok in plaats
  van de app.

  Dus: steeds lezen tot de regel er staat, en bij het antwoord zeggen hoe lang
  het duurde. Een melding die nooit komt is dan een echte fout en geen ongeluk.
#>
function WachtOpTekst($id, $patroon, $seconden = 20) {
  $begin = [DateTime]::UtcNow
  $pogingen = 0
  while (([DateTime]::UtcNow - $begin).TotalSeconds -lt $seconden) {
    $pogingen++
    $regels = @(Texts $id | Where-Object { $_ -match $patroon })
    if ($regels.Count -gt 0) {
      Write-Host ("   gevonden na {0:N1} s, in {1} pogingen" -f `
        ([DateTime]::UtcNow - $begin).TotalSeconds, $pogingen)
      return ,$regels
    }
    Start-Sleep -Milliseconds 400
  }
  Write-Host "   NIET gevonden in $seconden s"
  return @()
}

<#
  Kwam het geluid echt uit? Lees `state.json` terug en zeg het.

  `ZetGeluidUit` schrijft dat bestand vóór het starten, en het is dus geen bewijs
  dat de app ernaar keek. De app schrijft het bestand zelf zodra er iets
  verandert, en aanmelden is zo'n iets: na het aanmelden staat er dus iets in
  dat de app heeft weggeschreven. Dat verschil tussen "neergezet" en
  "weggeschreven" is de hele reden dat hier nog eens gekeken wordt.

  Wordt los aangeroepen, want drie proefjes melden zich zelf aan in plaats van
  via `StartEnMeldAan` en willen dit ook weten.
#>
function ControleerGeluidUit {
  $bestand = Join-Path $env:MABC_DATA 'state.json'
  try {
    $staat = Get-Content $bestand -Raw | ConvertFrom-Json
  } catch {
    Write-Host "   LET OP: $bestand is niet te lezen, dus het geluid is niet na te gaan."
    return $false
  }
  if ($staat.muted -eq $true) {
    Write-Host '   de app heeft het geluid uitgezet; dat staat in zijn eigen bestand.'
    return $true
  }
  Write-Host '   LET OP: de app speelt hoorbaar; hij heeft het geluid niet uitgezet.'
  return $false
}

<#
  Zegt wat de geluidsknop op het scherm vermeldt.

  Los van `ControleerGeluidUit`, en om een andere reden: die leest een bestand en
  `state.json` kan van alles zijn. Deze leest de knop zelf, en dat is wat iemand
  ziet. De knop zegt "Sound off" of "Geluid uit" als het geluid uit staat, en dat
  is de stand van het teken bij (`player.js:179` doet hetzelfde).

  Kan alleen als er een boek open is: de spelerbalk staat pas dan op het scherm,
  en zonder balk is er geen knop. Dat is geen tegenzwaarte maar de reden dat
  `ControleerGeluidUit` bestaat — die werkt meteen na het aanmelden, en deze pas
  als er iets te luisteren valt.
#>
function ControleerDempenOpHetScherm($id) {
  $regel = & $uia -ProcessId $id -Action text -Id SpelerDempen 2>$null
  if ($regel -notmatch "'([^']*)'") {
    Write-Host '   LET OP: er staat geen geluidsknop op het scherm, dus de stand is niet na te gaan.'
    return $false
  }
  $naam = $Matches[1]
  "   de geluidsknop zegt: '$naam'"
  if ($naam -match '^(Sound off|Geluid uit)$') {
    Write-Host '   goed: het scherm zegt dat het geluid uit staat.'
    return $true
  }
  Write-Host '   LET OP: het scherm zegt niet dat het geluid uit staat.'
  return $false
}

<#
  Start de app op de proefserver en meldt aan, en zegt het hardop als dat niet lukt.

  Elk proefje doet dit zelf, en niet "de aanmelding blijft van de vorige keer
  staan". Die aanname klopte toen de proefserver één keer voor de hele ronde
  gestart werd. Nu start elk proefje hem zelf, en de proefserver houdt zijn
  sessies in het geheugen (`fixtures/app-probe.mjs`: `const sessions = new
  Set()`), dus na een herstart kent hij geen enkele cookie meer en stuurt hij
  iedereen terug naar het aanmeldingsscherm. `boek-dat-omgezet-moet-worden` en
  `boek-uit` zagen dat als "de titel is er niet" en als "er speelt niets", en
  meldden twee klachten die allebei over het verkeerde onderwerp gingen.

  Dus geen proefje leent nog een sessie. Elk meldt zich aan, en de functie
  controleert daarna of het scherm echt weg is — want een mislukte aanmelding
  die niet wordt opgemerkt maakt de rest van het proefje zinloos.
#>
function StartEnMeldAan {
  $app = StartDeApp
  Start-Sleep -Seconds 14

  & $uia -ProcessId $app.Id -Action type -Id GateName -Value 'frank' 2>$null | Out-Null
  & $uia -ProcessId $app.Id -Action type -Id GatePass -Value 'probe' 2>$null | Out-Null
  & $uia -ProcessId $app.Id -Action invoke -Id GateGo 2>$null | Out-Null
  Start-Sleep -Seconds 6

  if ((Texts $app.Id) -match 'Sign in to carry on') {
    Write-Host '   MISLUKT: niet aangemeld, dus dit proefje meet niets meer.'
    Write-Host '   De app vroeg opnieuw om aanmelden. Dat kan de proefserver zijn die'
    Write-Host '   opnieuw is gestart: die vergeet zijn sessies bij elke start.'
    return $null
  }

  ControleerGeluidUit | Out-Null
  return $app
}

<#
  Vraagt de proefserver wat hij doet met /api/stream, en zegt het.

  Dit is geen extra proefje maar een voorwaarde, en hij staat in elk proefje dat
  iets wil horen. De reden is een ronde waarin er drie proefjes na elkaar
  draaiden en de eerste twee "groen" waren zonder dat er één seconde geluid was:
  er draaide een proefserver die niet deed wat de andere deed, en dat is alleen
  terug te zien in het verzoekenlog. De proefjes die er daarna bij kwamen zeiden
  niets, want ze vroegen het niet.

  `/api/stream` en niet `/api/mp3`, want dat is de route die de site gebruikt en
  de enige die de draaiende server heeft. Een proefje dat /api/mp3 wil testen
  start de server zelf met die schakelaar aan.

  Dus: voordat er geklikt wordt, even vragen. Duurt een halve seconde en het
  scheelt een middag.

  De uitkomst is een `$true` of een `$false`, en de tekst gaat met `Write-Host`.
  Dat is niet voor de mooierijk: het proefje doet `$klaar = VraagDeServerOm
  Geluid`, en dan verdwijnt alles wat de functie teruggeeft in die variabele.
  Zo'n functie die zijn eigen uitleg opslokt bij het enige gebruik dat hij heeft,
  zegt alleen iets als je hem toevallig niet aan een variabele bindt.
  `Write-Host` gaat altijd naar het scherm.
#>
function VraagDeServerOmGeluid {
  $code = curl.exe -s -o NUL -w '%{http_code}' 'http://127.0.0.1:8532/api/stream/11' 2>$null
  if (-not $code) {
    Write-Host '   LET OP: de proefserver antwoordt niet op poort 8532. Laat dit proefje hem zelf starten met StartProefserver.'
    return $false
  }
  if ($code -eq '404') {
    Write-Host '   LET OP: deze proefserver geeft geen geluid op /api/stream. Er gaat niets'
    Write-Host '   spelen en elk proefje dat op afspelen klikt zegt straks dat er niets'
    Write-Host '   gebeurde. Zet de proefserver opnieuw op met proefserver-aanzetten.ps1.'
    return $false
  }
  $soort = curl.exe -s -o NUL -w '%{content_type}' -r 0-0 'http://127.0.0.1:8532/api/stream/11' 2>$null
  Write-Host "   de proefserver geeft geluid op /api/stream (HTTP $code, $soort)"
  return $true
}

function VraagDeServerOmMp3 {
  $code = curl.exe -s -o NUL -w '%{http_code}' 'http://127.0.0.1:8532/api/mp3/11' 2>$null
  if ($code -eq '404') {
    Write-Host '   LET OP: deze proefserver heeft geen /api/mp3. Dat is de standaard, want de'
    Write-Host '   draaiende server heeft die route ook niet. Voor dit proefje moet hij aan:'
    Write-Host '   proefserver-aanzetten.ps1 -Mp3Route'
    return $false
  }
  Write-Host "   de proefserver heeft /api/mp3 (HTTP $code)"
  return $true
}

<#
  Start de proefserver en zeg of hij er is.

  Elk proefje dat de server nodig heeft, start hem zelf. Vóór deze regel was
  dat een losse handeling die je eerst moest doen, en dan werkte een proefje
  alleen als iemand eraan dacht. Drie proefjes op een rij waren daarna twee
  groene en één rode die "geen mp3-verzoeken" zeiden — en die rode ging niet
  eens over mp3: er draaide gewoon geen server, en de app zei "the collection
  server is not answering". Een proefje dat een voorwaarde stelt en die
  voorwaarde niet kan afdwingen, is een proefje dat over iets anders oordeelt.
#>
function StartProefserver([switch]$GeenFavorieten, [switch]$Mp3Route, [switch]$GeenStream) {
  Write-Host '   proefserver starten'
  if ($GeenStream) { & "$PSScriptRoot\proefserver-aanzetten.ps1" -GeenStream | Out-Null }
  elseif ($Mp3Route) { & "$PSScriptRoot\proefserver-aanzetten.ps1" -Mp3Route | Out-Null }
  elseif ($GeenFavorieten) { & "$PSScriptRoot\proefserver-aanzetten.ps1" -GeenFavorieten | Out-Null }
  else { & "$PSScriptRoot\proefserver-aanzetten.ps1" | Out-Null }
  Start-Sleep -Milliseconds 500
  return (VraagDeServerOmGeluid)
}
