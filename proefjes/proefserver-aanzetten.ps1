# Zet de proefserver aan.
#
# De app praat met een echte collectieserver. De Electron-app heeft er zelf een
# (MyAudiobooks\server), maar die spreekt de routes van de uitgezette versie
# niet aan: geen /api/account/signin, geen /api/favourites. De proefserver
# fixtures\app-probe.mjs in de Electron-repo doet dat wel, en is daarmee de
# enige manier om de app hier volledig mee te testen.
#
# Eerst dit draaien, dan een van de proefjes. De proefjes kijken in requests.log
# wat de server zag, en daarom moet de server zijn lijst hier in deze map
# schrijven.
#
# De proefjes die de server nodig hebben starten hem zelf, via `StartProefserver`
# in `hulp.ps1`. Handmatig draaien mag nog, maar het hoeft niet meer: het moest
# wel, en dat maakte de proefjes afhankelijk van wat iemand eerder had gedaan.
# `requests.log` wordt hierbij elke keer leeg gemaakt, dus twee proefjes tegelijk
# kunnen niet — en dat is een reden om ze één voor één te draaien, niet om ze
# achter elkaar te mogen draaien zonder de server opnieuw op te zetten.
param(
  [int]$Poort = 8532,
  # Waar de proefserver staat. Leeg betekent: naast dit project, in `fixtures`.
  #
  # Hier stond eerst een vast pad naar `B:\_OpenCode\Projects\MyAudiobooks\
  # fixtures\app-probe.mjs` — de Electron-map, waar de proefserver vandaan komt en
  # waar hij in `.gitignore` staat. Dat maakte de proefjes afhankelijk van een map
  # die niet in deze repo zit, dus na een klon werkte er geen enkel proefje meer.
  #
  # `..\fixtures` is nu de standaard en werkt overal heen. `-Server` blijft er,
  # want een proefje dat iets wil vergelijken wijst liever even naar een
  # proefserver elders dan naar de standaard.
  [string]$Server = '',
  [switch]$GeenFavorieten,
  [switch]$Mp3Route,
  [switch]$GeenStream
)

$ErrorActionPreference = 'Stop'
Stop-Process -Name node -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500
Remove-Item "$PSScriptRoot\requests.log" -ErrorAction SilentlyContinue

$env:APP_PROBE_PORT = "$Poort"
$env:APP_PROBE_LOG = "$PSScriptRoot\requests.log"
$env:APP_PROBE_KEEP = '1'
# Zonder deze schakelaar geeft de server op /api/favourites een gewone 404 met
# HTML, zoals een server die de route niet kent. Dat is wat
# geen-favorieten.ps1 meet: dat het hart dan verdwijnt en de app er niets van
# zegt.
$env:APP_PROBE_NO_FAVS = if ($GeenFavorieten) { '1' } else { $null }
# En deze óók, en dat is geen gewoonte maar een les: een proefje zette deze
# schakelaar en ruimde hem niet op, waarna élke volgende proef een server met
# die schakelaar startte en `aanmelden-en-afspelen.ps1` braaf "mp3: 0" zei en
# doorging. De schakelaar begint hier dus altijd uit, en alleen een proefje dat
# de mp3-route echt wil zet hem aan.
$env:APP_PROBE_MP3 = if ($Mp3Route) { '1' } else { $null }
# En deze derde ook, uit dezelfde reden. Zij zet /api/stream stuk, zodat geen van
# beide geluidsroutes werkt; `geen-geluid.ps1` meet wat de app dan zegt.
$env:APP_PROBE_NO_STREAM = if ($GeenStream) { '1' } else { $null }

if (-not $Server) { $Server = Join-Path (Split-Path $PSScriptRoot -Parent) 'fixtures\app-probe.mjs' }
if (-not (Test-Path $Server)) {
  # Niet: "geen server, dus dit proefje meet niets" en dan doorlopen. Dat is een
  # proefje dat stil zegt dat er niets te meten viel. Zonder de server kan er
  # niets kloppen, en dat is hier de reden om het script te onderbreken.
  "  de proefserver staat niet op $Server"
  "  Zet -Server op het pad van app-probe.mjs, of draai 'npm install' in de map"
  "  van dit project zodat node_modules bij fixtures\node_modules staat."
  throw "proefserver ontbreekt op $Server"
}
# `node_modules` moet bij de server kunnen worden gevonden, en node kijkt vanaf de
# map van het script. Dus draait hij vanuit de projectmap, niet vanuit `fixtures`.
$fx = Start-Process -FilePath 'node' -ArgumentList $Server -PassThru -WindowStyle Hidden `
      -WorkingDirectory (Split-Path $PSScriptRoot -Parent)

Start-Sleep -Seconds 2
$luistert = (Test-NetConnection -ComputerName 127.0.0.1 -Port $Poort -WarningAction SilentlyContinue).TcpTestSucceeded
if (-not $luistert) {
  # Waarom zeggen, en niet alleen dát hij niet luistert.
  #
  # Dit is niet hypothetisch. De proefserver stond een keer in de Electron-map,
  # en toen die er niet meer was, zeiden de proefjes "geen server, dus dit
  # proefje meet niets" en gingen door: elf regels groen, en geen van die regels
  # over de app. En de allereerste keer dat hier de fixture naast het project
  # kwam te staan, miste `node_modules` — `express` was nergens te vinden, node
  # stopte meteen, en de enige melding was "luistert niet op poort 8532". Dat is
  # waar, en het zegt niets: er zijn minstens drie redenen waarom dat zo is, en
  # ze vragen alle drie om een andere handeling.
  $uitleg = @(
    "  de proefserver draait niet op poort $Poort. Zijn proces is:"
    "    pid $($fx.Id), draait nog: $(-not $fx.HasExited)"
  )
  if ($fx.HasExited) {
    $uitleg += '  hij is al gestopt. De meest waarschijnlijke reden is dat de'
    $uitleg += '  afhankelijkheden ontbreken. Draai dit in de map van het project:'
    $uitleg += '      npm install'
    $uitleg += "  (en kijk of 'node' zelf bestaat: $(if (Get-Command node -ErrorAction SilentlyContinue) { 'ja' } else { 'nee' }))"
  } else {
    $uitleg += "  hij draait nog maar luistert niet. Kijk of poort $Poort al door"
    $uitleg += '  iets anders bezet is, of zet -Server op een andere -Poort.'
  }
  $uitleg += "  node draait met deze werkmap: $(Split-Path $PSScriptRoot -Parent)"
  $uitleg += "  en kijkt daarom naar $(Join-Path (Split-Path $PSScriptRoot -Parent) 'node_modules')"
  $uitleg | ForEach-Object { Write-Host $_ }
  throw "proefserver draait niet op poort $Poort"
}

"proefserver draait (pid $($fx.Id)) op http://127.0.0.1:$Poort"
"  wachtwoord: probe   gebruikersnaam: maakt niet uit"
"  lijst van verzoeken: $PSScriptRoot\requests.log"
if ($GeenFavorieten) { '  /api/favourites geeft 404, dus het hart blijft weg' }
if ($Mp3Route) { '  /api/mp3 geeft mp3, en twee keer 409 voor het omzetten' }
else { '  /api/mp3 bestaat niet, zoals op de draaiende server' }
if ($GeenStream) { '  /api/stream bestaat ook niet: er is geen enkele geluidsroute' }
else { '  /api/stream geeft het bestand zoals het op schijf ligt, en Range' }
'  app starten met MABC_URL=http://127.0.0.1:' + $Poort
