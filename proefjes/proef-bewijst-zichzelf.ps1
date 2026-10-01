# Kijkt of `voortgang-van-server.ps1` iets merkt als het níét zo is.
#
# Een proefje dat alleen groen kan zeggen meet niets, en dat is hier geen
# theorie maar de reden dat dit bestand bestaat. Zie onderaan: er waren twee
# manieren waarop dit script zelf zíjn oordeel misleidde, en allebei waarbij de
# uitkomst "groen" was terwijl er niets aan de app te meten viel.
#
# Dus: zet de tweede voortgang-boekhouding terug in de app — het `place`-veld in
# `Store.cs` plus het gebruik ervan in `Player.cs` — bouw, en draai dan het
# proefje. Het proefje hoort een MISLUKT te geven. Geeft het groen, dan meet het
# niets en moet het proefje eerst sterker.
#
# Destructief: dit schrijft in `Store.cs` en `Player.cs` en bouwt de exe. De
# bronnen worden teruggezet, ook als er iets misgaat. Draai het dus niet terwijl
# je aan die bestanden werkt, en niet naast `alle.ps1`.
#
# Gebruik:
#   .\proef-bewijst-zichzelf.ps1
$ErrorActionPreference = 'Stop'

$map = Split-Path $PSScriptRoot -Parent
$werk = Join-Path $env:TEMP 'proef-bewijst-zichzelf'

<#
  Waar de `dotnet.exe` staat die dit script gebruikt.

  Hier stond een vast pad in de thuismap van één machine. Dat is hetzelfde
  probleem als de twee paden die eerder in de proefjes stonden en die zijn
  verwijderd: het script werkt dan alleen nog op de computer van degene die het
  geschreven heeft, en op elke andere computer zegt het "ONGELDIG: dotnet staat
  niet op …" — wat waar is, en niet zegt wat er dan wél moet gebeuren.

  Dus: eerst de gewone `dotnet` van PATH, want daar staat op elke machine met de
  SDK de goede versie in. Pas als die er niet is, kijkt het script of er een
  SDK-installatie in de map van deze gebruiker staat, want die legt Microsoft daar
  neer en daar staat de padcode niet in. En als het er dan nog niet is, zegt het
  dat met de handeling erbij.
#>
function VindDotnet {
  $opPad = Get-Command dotnet -ErrorAction SilentlyContinue
  if ($opPad) { return $opPath.Source }
  $inMap = Get-ChildItem -Path (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe') -ErrorAction SilentlyContinue
  if ($inMap) { return $inMap.FullName }
  $programFiles = Join-Path ${env:ProgramFiles} 'dotnet\dotnet.exe'
  if (Test-Path $programFiles) { return $programFiles }
  return $null
}
$dn = VindDotnet
if (-not $dn) {
  "ONGELDIG: dotnet.exe is niet gevonden."
  "  Installeer de .NET 10 SDK, of zet het pad ernaartoe in PATH. Zonder de SDK"
  "  kan dit script de app niet bouwen, en het proefje dat het wil controleren"
  "  niet draaien."
  exit 1
}

New-Item -ItemType Directory -Path $werk -Force | Out-Null
Copy-Item "$map\Store.cs" "$werk\Store.cs" -Force
Copy-Item "$map\Player.cs" "$werk\Player.cs" -Force

<#
  Bouw, en zeg het ronduit als het misgaat.

  De eerste versie van dit script deed `& dotnet publish … | Select-Object -Last 3`
  en liep daarna gewoon door. De sabotage compileerde niet, de build mislukte,
  en het proefje draaide tegen de oude binary: groen. Erger dan geen proefje,
  want dit script zou "het proefje merkt het niet" hebben geschreven over een
  app die niet eens gebouwd was.

  Dus: de uitvoer wordt gecontroleerd, en een mislukte build stopt het script.
#>
function Publiceer([string]$label) {
  Stop-Process -Name MyAudiobooks -Force -ErrorAction SilentlyContinue
  Start-Sleep -Milliseconds 400

  # De bronbestanden aanraken, en dat is niet cosmetiek. `Copy-Item` zet de
  # inhoud terug maar houdt de oude `LastWriteTime`, en MSBuild besluit dan dat de
  # build nog klopt en herverpakt de vorige `MyAudiobooks.dll`. Bij de eerste
  # poging gebeurde precies dat: de bronnen waren weer schoon, de exe niet, en
  # dit script concludeerde dat het proefje de tweede boekhouding niet merkt —
  # over een build die er nog in stond.
  Get-ChildItem "$map\*.cs" | ForEach-Object { $_.LastWriteTime = Get-Date }

  $uitvoer = & $dn publish "$map\MyAudiobooks.csproj" -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -o "$map\uit" --nologo 2>&1 | Out-String
  if ($uitvoer -match ': error ' -or $uitvoer -match 'Build FAILED') {
    "ONGELDIG: de build mislukte ($label), dus er is niets gebouwd om te meten."
    $uitvoer -split "`r?`n" | Where-Object { $_ -match ': error ' } |
      Select-Object -First 6 | ForEach-Object { "   $_" }
    throw 'build mislukte; het script stopt, want een proefje tegen de vorige exe zegt niets'
  }
  "   gebouwd ($label)"
}

function Sabotageer {
  # LF, geen CRLF: alle bestanden in deze map zijn LF. Een CR die toevallig in een
  # vervanging sluipt bouwt prima maar maakt de wijziging onleesbaar, en `Replace`
  # met een patroon dat niet voorkomt doet niets zonder iets te zeggen — dus elke
  # vervanging wordt gecontroleerd.
  $store = [System.IO.File]::ReadAllText("$map\Store.cs")
  $vorig = $store
  $store = $store.Replace(
    '    public string User { get; private set; } = "";',
    "    public string User { get; private set; } = `"`";`n    public BookPlace? Place { get; private set; }`n`n    public sealed class BookPlace`n    {`n        [JsonPropertyName(`"bookId`")] public long BookId { get; set; }`n        [JsonPropertyName(`"trackIdx`")] public int TrackIdx { get; set; }`n        [JsonPropertyName(`"position`")] public int Position { get; set; }`n    }`n")
  $store = $store.Replace('            user = User,', "            user = User,`n            place = Place,")
  $store = $store.Replace(
    '    public void SetSound(double volume, bool muted)',
    "    public void SetPlace(BookPlace? place) { Place = place; Write(); }`n`n    public void SetSound(double volume, bool muted)")
  if ($store -eq $vorig) { throw 'de sabotage van Store.cs greep nergens' }
  [System.IO.File]::WriteAllText("$map\Store.cs", $store, [System.Text.UTF8Encoding]::new($false))

  $player = [System.IO.File]::ReadAllText("$map\Player.cs")
  $vorig = $player
  # `secondsFromStart` en niet `from`: die variabele is weg sinds het afspelen
  # alleen nog de seconden van de server gebruikt. De eerste poging gebruikte
  # `from`, compileerde niet, en leverde dus een proefje op over de oude binary.
  $player = $player.Replace(
    '        Open(track, Math.Max(0, secondsFromStart), autoplay);',
    "        if (_store.Place is { } kept && kept.BookId == book.Id`n            && kept.TrackIdx == part && kept.Position > 0)`n        {`n            secondsFromStart = kept.Position;`n        }`n        Open(track, Math.Max(0, secondsFromStart), autoplay);")
  $player = $player.Replace(
    '        if (Book is null || _reader is null) return;',
    "        if (Book is null || _reader is null) return;`n        _store.SetPlace(new Store.BookPlace`n        {`n            BookId = Book.Id, TrackIdx = Part, Position = (int)Position.TotalSeconds,`n        });")
  if ($player -eq $vorig) { throw 'de sabotage van Player.cs greep nergens' }
  [System.IO.File]::WriteAllText("$map\Player.cs", $player, [System.Text.UTF8Encoding]::new($false))
}

try {
  '=== de tweede voortgang-boekhouding terugzetten'
  Sabotageer
  Publiceer 'met de tweede boekhouding'

  '=== voortgang-van-server.ps1 draaien, en kijken of hij het merkt'
  Push-Location $PSScriptRoot
  $uitvoer = & powershell -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot\voortgang-van-server.ps1" 2>&1 | Out-String
  Pop-Location
  $uitvoer.TrimEnd() -split "`r?`n" |
    Where-Object { $_ -match 'MISLUKT|goed|place|begin|Resume' } |
    ForEach-Object { "   $_" }

  '=== oordeel over het proefje'
  if ($uitvoer -notmatch 'goed: de knop zegt Resume') {
    'ONGELDIG: het proefje kwam niet eens bij stap 3, dus het oordeel hieronder'
    'zegt niets over de tweede boekhouding maar over het proefje zelf.'
  }
  elseif ($uitvoer -match 'MISLUKT') {
    'goed: het proefje merkt de tweede boekhouding, dus het meet iets.'
  } else {
    'ONGELDIG: het proefje merkt de tweede boekhouding niet, dus het meet niets.'
    exit 1
  }
}
finally {
  Copy-Item "$werk\Store.cs" "$map\Store.cs" -Force
  Copy-Item "$werk\Player.cs" "$map\Player.cs" -Force
  Publiceer 'terug met de echte code'
  Remove-Item $werk -Recurse -Force -ErrorAction SilentlyContinue
}
