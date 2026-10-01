# Geen van beide geluidsroutes werkt, en de app zegt dat.
#
# Er zijn twee wegen om aan geluid te komen: `/api/mp3/<spoor>` (met voortgang
# tijdens het omzetten) en `/api/stream/<spoor>` (de weg die de site gebruikt).
# Deze proefserver heeft er geen van beide, en dan is de speler uit zijn
# gebruikte strip. Dat is het enige geval waarin er niets meer te proberen valt.
#
# Het is een proefje over één regel in het programma, en die regel is de laatste
# die nog ongetest was: wat zegt hij als beide wegen 404 geven? Het antwoord
# moet beide antwoorden van de server bevatten, want de eerste vraag is
# beantwoord met "de route bestaat niet" en de tweede met "kan niet GET
# /api/stream/11". Alleen de eerste zeggen is halve waarheid; alleen de tweede is
# een klacht zonder aanwijzing.
#
# En het zwijgen zou het ergste zijn. Er moet dus ook staan dat er niets speelde
# en dat er geen voortgang bewaard is — een speler die stilletjes niets doet is
# het waar dat deze hele app niet mag tonen.
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


Say 'proefserver aanzetten zonder /api/stream'
# `VraagDeServerOmGeluid` zegt hier terecht LET OP en geeft $false terug, en dat
# is geen fout van dit proefje: 404 op /api/stream is precies wat hij wil
# hebben. Dus hij wordt niet aangeroepen; de schakelaar wordt apart nagekeken.
Write-Host '   proefserver starten'
& "$probe\proefserver-aanzetten.ps1" -GeenStream | Out-Null
$mp3 = curl.exe -s -o NUL -w '%{http_code}' 'http://127.0.0.1:8532/api/mp3/11' 2>$null
$stream = curl.exe -s -o NUL -w '%{http_code}' 'http://127.0.0.1:8532/api/stream/11' 2>$null
Write-Host "   /api/mp3/11 geeft HTTP $mp3 en /api/stream/11 geeft HTTP $stream"
if ($mp3 -ne '404' -or $stream -ne '404') {
  '   MISLUKT: dit proefje meet niets als er nog een geluidsroute werkt.'
  Stop-Process -Name node -Force -ErrorAction SilentlyContinue
  exit 1
}

Stop-Process -Name MyAudiobooks -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 700
Remove-Item "$data\fouten.log" -ErrorAction SilentlyContinue
Remove-Item "$data\state.json" -Force -ErrorAction SilentlyContinue
Remove-Item "$data\cookie.bin" -Force -ErrorAction SilentlyContinue

Say 'app starten, aanmelden, een boek openen en spelen'
$p = StartEnMeldAan
if ($null -eq $p) { exit 1 }

& $open -ProcessId $p.Id -Title 'Het eerste boek' 2>$null | Select-Object -Last 1 | ForEach-Object { "   $_" }
Start-Sleep -Seconds 2
$n = LogCount
& $uia -ProcessId $p.Id -Action invoke -Id BoekAfspelen 2>$null | Out-Null

Say 'wat de app zegt'
# Wachten, niet een vaste tijd slapen: de melding staat zeven seconden, en een
# proefje dat er na zeven seconden kijgt leest dan een scherm waar hij al van af
# is. `WachtOpTekst` zegt hoe lang het duurde, dus een gemiste melding is een
# fout en geen ongeluk.
$regels = WachtOpTekst $p.Id 'stream|mp3|404|Cannot GET'
$regels | ForEach-Object { "   $_" }
$t = ($regels -join ' ')

if ($t -match '/api/stream' -or $t -match 'not give this part' -or $t -match 'gaf dit deel ook niet') {
  '   goed: er staat wat de tweede weg zei.'
} else {
  '   MISLUKT: er staat niets over /api/stream, dus de tweede vraag is niet beantwoord.'
}
if ($t -match 'mp3' -and $t -match '404') {
  '   goed: en er staat bij wat de eerste weg zei.'
} else {
  '   MISLUKT: er staat niets over de mp3-route, dus de helft van het antwoord ontbreekt.'
}

Say 'en beide wegen zijn geprobeerd, want het is niet één willekeurige fout'
$regelsLog = LogSince $n
$mp3Keer = @($regelsLog | Where-Object { $_ -match '/api/mp3/11' }).Count
$streamKeer = @($regelsLog | Where-Object { $_ -match '/api/stream/11' }).Count
"   /api/mp3/11 gevraagd: $mp3Keer keer   /api/stream/11 gevraagd: $streamKeer keer"
$regelsLog | Where-Object { $_ -match 'mp3/11|stream/11' } | ForEach-Object { "   $_" }
if ($mp3Keer -lt 1) { '   MISLUKT: de app vroeg niet eens om mp3, dus hij gaf te vroeg op.' }
if ($streamKeer -lt 1) { '   MISLUKT: de app vroeg niet om /api/stream, dus hij gaf na de eerste 404 op.' }

Say 'en er speelde niets, en dat is te zien'
$voortgang = @($regelsLog | Where-Object { $_ -match '/api/progress' }).Count
"   voortgang bewaard: $voortgang (moet 0 zijn: er is niets gespeeld)"
if ($voortgang -gt 0) { '   MISLUKT: er is voortgang bewaard, dus er is toch gespeeld.' }
else { '   goed: er is niets gespeeld en dus niets bewaard.' }

Say 'fouten'
Foutenlijst

Stop-Process -Name MyAudiobooks -Force -ErrorAction SilentlyContinue
Stop-Process -Name node -Force -ErrorAction SilentlyContinue
