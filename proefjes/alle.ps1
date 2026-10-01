# Draait alle proefjes, een voor een, en zegt welke er kloppen.
#
# Zonder dit bestand draaide het proefje voor proefje, in de volgorde waarin ze
# toevallig ontstonden, en de lijst groeide er stilletjes bij. Twee dingen daarvan
# hebben een test laten slagen die niets beweerde: een proefje dat alleen telde
# en niet oordeelde, en een proefje dat een schakelaar in de omgeving liet
# staan voor het volgende.
#
# Dus twee regels die hier de moeite waard zijn:
#
#   1. de oordeelwoorden worden gezocht in de uitvoer, niet in het hart van de
#      proefjes. "MISLUKT" en "ONGELDIG" moeten nergens staan, en dat is hier
#      één plek om dat te zien in plaats van tien.
#   2. na elk proefje sluit de app en de server. Een proefje dat een proefserver
#      laat staan, bepaalt de uitkomst van het volgende.
#
# De volgorde is die van wat het meest weegt: eerst of er geluid is, dan of het
# geluid de juiste vorm heeft, dan of de rest van het scherm werkt.
param([string[]]$Alleen)

$ErrorActionPreference = 'Continue'
$alle = @(
  'geen-mp3-route',        # er speelt geluid, ook zonder mp3-route
  'hervatten',             # en het begint waar je gebleven was
  'voortgang-van-server',  # en die plek komt van de server, niet van een eigen lijst
  'niet-mp3',              # en een m4a zegt wat hij is, met wat je eraan kunt doen
  'geen-geluid',           # en als geen van beide wegen werkt, zegt hij dat
  'boek-dat-omgezet-moet-worden',  # de 409-route, op een server die hem heeft
  'aanmelden-en-afspelen', # de hele route in een keer
  'boek-uit',              # een boek dat uitkomt gaat van de plank af
  'geen-favorieten',       # een route die er niet is zorgt voor zichzelf
  'zoeken',                # zoeken doet iets
  'een-proces'             # en het is één proces
)
if ($Alleen) { $alle = $Alleen }

# Eerst de BOM's en de syntaxis, want een proefje dat niet parseert doet niets en
# zegt niets, en dan lijkt het alsof er niets te meten viel.
& "$PSScriptRoot\bom.ps1" | ForEach-Object { "   $_" }

$rode = 0
foreach ($naam in $alle) {
  $bestand = "$PSScriptRoot\$naam.ps1"
  if (-not (Test-Path $bestand)) { "### ONBEKEND proefje: $naam"; $rode++; continue }

  "`n################################################ $naam"
  $uitvoer = & powershell -NoProfile -ExecutionPolicy Bypass -File $bestand 2>&1 | Out-String
  $uitvoer.TrimEnd() | ForEach-Object { $_ }

  # Na elk proefje weg: de app en de server. Een proefje dat een server laat
  # staan, bepaalt de uitkomst van het volgende.
  Stop-Process -Name MyAudiobooks -Force -ErrorAction SilentlyContinue
  Stop-Process -Name node -Force -ErrorAction SilentlyContinue
  Start-Sleep -Milliseconds 400

  $fouten = @()
  $regels = $uitvoer -split "`r?`n"
  # De dubbele punt is er niet voor de sier. `zoeken.ps1` schrijft "gezegd wat er
  # niet gevonden is" als het wél goed ging, en dat maakte het proefje rood op
  # zijn eigen succes. De woorden die een probleem betekenen eindigen allebei op
  # een dubbele punt: `NIET GEVONDEN: id=…` uit `uia.ps1` en `TITEL NIET
  # GEVONDEN: …` uit `open.ps1`.
  $fouten += $regels | Where-Object { $_ -match 'MISLUKT|ONGELDIG|\bNIET GEVONDEN:' }
  # En ook op het oordeelwoord zoals het hoort te heten, zodat een proefje dat het
  # verkeerd spelt (er waren er drie, met zeven keer een typefout ertussen) niet
  # meer als groen langskomt. `bom.ps1` spoort dat nu al op voordat er gedraaid
  # wordt, maar dit is de plek waar zo'n woord zichtbaar zou worden.
  #
  # Met `-cmatch`, want het oordeel staat in hoofdletters en gewone tekst als
  # "mislukt" of "mislukte" hoort bij. Het patroon is hetzelfde als dat van
  # `bom.ps1`, en zoekt op `LUK` zodat `MISLKT` ook gevonden wordt.
  $fouten += $regels | Where-Object { $_ -cmatch '\b[A-Z]*LUK[A-Z]*\b' -and $_ -notmatch 'MISLUKT' }
  if ($fouten.Count -gt 0) {
    $rode++
    "### oordeel: rood ($($fouten.Count) regel(s))"
    $fouten | Select-Object -First 8 | ForEach-Object { "     $_" }
  } else {
    '### oordeel: groen'
  }
}

"`n################################################"
if ($rode -eq 0) { "ALLE PROEFJES GROEN ($($alle.Count))"; exit 0 }
"$rode van $($alle.Count) proefjes rood"
exit 1
