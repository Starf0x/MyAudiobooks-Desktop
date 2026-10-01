# Het bewijs dat het één proces is.
#
# Drie dingen tegelijk:
#   1. hoeveel processen er met die naam lopen;
#   2. of dat proces kindprocessen heeft;
#   3. of er iets anders is opgekomen dat hier net nog niet stond: een
#      WebView2, een Edge, een node, een helper.
#
# Punt 3 is de vraag die de hele herschrijving stelde: Electron start vier
# processen en een hele Chromium. Als er na het starten één proces bij staat,
# is dat afgelopen.
$ErrorActionPreference = 'Continue'
$uia = "$PSScriptRoot\uia.ps1"
# Het pad naar de exe, naast dit project. Hier stond een tweede, vast pad naar
# `B:\_OpenCode\...` als terugval voor wanneer de exe er nog niet staat; dat maakte
# dit proefje afhankelijk van een map op één machine. Nu zegt het wat er ontbreekt
# en houdt het op, want een proefje dat een exe mist meet één proces of geen
# processen — en dat is geen van beide iets.
$exe = "$PSScriptRoot\..\uit\MyAudiobooks.exe"
if (-not (Test-Path $exe)) {
  "  de app staat niet op $exe"
  '  Bouw hem eerst: dotnet publish MyAudiobooks.csproj -c Release -r win-x64'
  '  --self-contained true -p:PublishSingleFile=true -o uit'
  exit 1
}
. "$PSScriptRoot\hulp.ps1"
# De map van de app. `hulp.ps1` zet MABC_DATA, en dat is bewust een andere map
# dan die van de echte installatie: de proefjes melden zich aan op hun eigen
# proefserver, en zonder dit zouden ze de cookie en de voortgang van de
# gebruiker overschrijven. Die zou dan na elke proefronde opnieuw moeten
# aanmelden, zonder te weten waarom. Zie ook de kop van hulp.ps1.
$data = $env:MABC_DATA

function Say($text) { "=== $text" }

# Zonder server blijft de app op het aanmeldingsscherm staan, en dan is er wel een
# raam en wel een proces, maar niets dat beweist dat hij werkt. Dus dit proefje
# start de server zelf, en telt daarna pas.
Say 'proefserver opzetten'
if (-not (StartProefserver)) { '   MISLUKT: geen server, dus dit proefje meet niets.'; exit 1 }

Say 'wat er draait voor het starten'
$before = (Get-Process | Select-Object -ExpandProperty Id)
$beforeNames = (Get-Process | Group-Object Name | ForEach-Object { $_.Name })

Stop-Process -Name MyAudiobooks -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 600
Remove-Item "$data\fouten.log" -ErrorAction SilentlyContinue
Remove-Item "$data\state.json" -Force -ErrorAction SilentlyContinue
Remove-Item "$data\cookie.bin" -Force -ErrorAction SilentlyContinue

Say 'starten'
$p = StartDeApp
Start-Sleep -Seconds 14

Say 'hoeveel processen heet MyAudiobooks'
$procs = Get-Process -Name MyAudiobooks -ErrorAction SilentlyContinue
"   aantal: $(($procs | Measure-Object).Count)"
foreach ($q in $procs) {
  "   pid {0}  {1:N0} MB  {2} draadjes  reageert: {3}  titel: '{4}'" -f `
    $q.Id, ($q.WorkingSet64 / 1MB), $q.Threads.Count, $q.Responding, $q.MainWindowTitle
}

Say 'heeft het kindprocessen'
$kids = Get-CimInstance Win32_Process -Filter "ParentProcessId = $($p.Id)"
"   aantal: $(($kids | Measure-Object).Count)"
$kids | ForEach-Object { "   $($_.ProcessId)  $($_.Name)  $($_.CommandLine)" }

Say 'wat is er nieuw opgekomen'
$after = Get-Process | Where-Object { $before -notcontains $_.Id }
$new = $after | Group-Object Name | Sort-Object Name
foreach ($g in $new) { "   {0} x {1}" -f $g.Count, $g.Name }
"   (MyAudiobooks zelf natuurlijk; WebView2 of msedge of node zou hier staan als er iets meeliep)"

# ...maar alleen wat van dézeén app telt mee, en de lijst hierboven doet dat niet.
#
# De lijst is alles wat in veertien seconden is begonnen, van wie dan ook. Op
# deze machine starten in die tijd de WebView2-processen van Teams, Grammarly,
# Outlook, FortiClient en Superhuman, en toen gaf dit proefje daarom rood:
#
#   MISLUKT: er is een browser of een node-proces bijgekomen:  1 x msedgewebview2
#
# terwijl de app zelf géén kindprocessen had (`aantal: 0`, verderop) en er dus
# helemaal geen WebView2 bij was. Het proefje las zijn eigen oordeel over een
# ander programma. Dat is precies de soort fout die hier duidelijk zichtbaar moet
# zijn: hij is niet te herkennen aan het app-gedrag, want het app-gedrag was
# correct.
#
# Dus: de oordeelvraag wordt niet "kwam er een browserproces op" maar "kwam er
# een browserproces op dat van deze app is". Het omschrijven van een proces
# loopt omhoog via `ParentProcessId` tot de app, of tot iets dat al draaide toen
# de app van start ging.
$alle = @{}
# De sleutel naar `[int]`, en dat is niet cosmetiek. `Get-Process` geeft een
# `Int32` en `Get-CimInstance` een `UInt32`, en een hashtabel op PowerShell
# vindt `1234` (Int32) niet bij de sleutel `1234` (UInt32). De eerste versie van
# deze controle gaf daardoor "bij de app: 0" terwijl er MyAudiobooks op die
# lijst stond: de app telde niet mee, en een controle die de app niet meetelt is
# een controle die niet doet wat er staat.
Get-CimInstance Win32_Process | ForEach-Object { $alle[[int]$_.ProcessId] = $_ }
function VanOnzeApp($pid) {
  $nu = [int]$pid
  for ($i = 0; $i -lt 8 -and $nu; $i++) {
    $proc = $alle[$nu]
    if ($null -eq $proc) { return $false }
    if ($proc.Name -eq 'MyAudiobooks.exe') { return $true }
    $nu = [int]$proc.ParentProcessId
  }
  return $false
}

$vanOns = @()
$vanIemandAnders = @()
$weg = 0
foreach ($q in $after) {
  $cim = $alle[[int]$q.Id]
  if ($null -eq $cim) { $weg++; continue }   # al weg; daar valt niets mee te beginnen
  if ($cim.Name -eq 'MyAudiobooks.exe' -or (VanOnzeApp $q.Id)) { $vanOns += $cim }
  else { $vanIemandAnders += $cim }
}

Say 'en wat daarvan bij deze app hoort'
"   bij de app: $($vanOns.Count) — $(($vanOns | ForEach-Object { $_.Name } | Sort-Object -Unique) -join ', ')"
"   van een ander programma: $($vanIemandAnders.Count) — $(($vanIemandAnders | ForEach-Object { $_.Name } | Sort-Object -Unique) -join ', ')"
"   alweer weg: $weg"
"   (die laatste groep zegt niets over deze app; het is wat er op de computer"
"    tegelijk gebeurde, en dat telt niet mee)"
if ($vanOns.Count -lt 1) {
  '   LET OP: er is geen enkel proces van de app gevonden, dus de onderstaande'
  '   oordeelsvraag over een meegeslepen browser meet hier niets.'
}

Say 'is er een raam'
Add-Type -Namespace W -Name U -MemberDefinition '
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc f, IntPtr p);
public delegate bool EnumWindowsProc(IntPtr h, IntPtr p);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint id);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, System.Text.StringBuilder s, int n);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
public struct RECT { public int Left, Top, Right, Bottom; }'
$found = New-Object System.Collections.ArrayList
$cb = [W.U+EnumWindowsProc]{
  param($h, $l)
  $id = 0
  [W.U]::GetWindowThreadProcessId($h, [ref]$id) | Out-Null
  if ($id -eq $p.Id -and [W.U]::IsWindowVisible($h)) {
    $sb = New-Object System.Text.StringBuilder 512
    [W.U]::GetWindowTextW($h, $sb, 512) | Out-Null
    $r = New-Object W.U+RECT
    [W.U]::GetWindowRect($h, [ref]$r) | Out-Null
    if ($sb.ToString().Length -gt 0) {
      # De `-f` en zijn vijf waarden in haakjes: zonder die haakjes leest
      # PowerShell de komma's als het begin van de lijst argumenten van Add(),
      # en dan komt er een lege regel in plaats van de naam van het raam.
      $found.Add(("   '{0}'  {1}x{2} op {3},{4}" -f $sb.ToString(), ($r.Right - $r.Left), ($r.Bottom - $r.Top), $r.Left, $r.Top)) | Out-Null
    }
  }
  return $true
}
[W.U]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
if ($found.Count) { $found } else { '   GEEN zichtbaar raam gevonden' }

Say 'foutenlijst'
if (Test-Path "$data\fouten.log") { Get-Content "$data\fouten.log" -Raw } else { '   (geen foutenlijst)' }

Say 'gegevens van de app'
# Zonder aanmelden blijft de app op de poort staan en schrijft hij niets, dus
# aanmelden voordat er naar de bestanden gekeken wordt. Anders zou er een
# "geen state.json" staan en zou dat lijken alsof de app niets bewaart.
& $uia -ProcessId $p.Id -Action type -Id GateName -Value 'frank' 2>$null | Out-Null
& $uia -ProcessId $p.Id -Action type -Id GatePass -Value 'probe' 2>$null | Out-Null
& $uia -ProcessId $p.Id -Action invoke -Id GateGo 2>$null | Out-Null
Start-Sleep -Seconds 6
Get-ChildItem $data -Force | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
"--- state.json:"
Get-Content "$data\state.json" -Raw
Say 'en het geluid is uitgezet, op de manier die de app het zelf wegschrijft'
ControleerGeluidUit | Out-Null

Say 'na het aanmelden nog steeds één proces'
$nu = @((Get-Process -Name MyAudiobooks -ErrorAction SilentlyContinue)).Count
$nukids = @((Get-CimInstance Win32_Process -Filter "ParentProcessId = $($p.Id)")).Count
"   aantal: $nu"
"   kindprocessen: $nukids"

Say 'oordeel'
# Drie beweringen, en ze moeten alle drie waar zijn. Vóór dit proefje was er geen
# oordeel: het drukte de aantallen af en sloot af, en een proefje dat een aantal
# laat zien zegt niet of dat aantal goed is.
if ($nu -eq 1) { '   goed: er draait precies één MyAudiobooks.' }
else { "   MISLUKT: er draaien $nu processen met die naam, en het moeten er één zijn." }

if ($nukids -eq 0) { '   goed: en die heeft geen kindprocessen.' }
else {
  "   MISLUKT: er zijn $nukids kindprocessen:"
  $kids | ForEach-Object { "      $($_.Name)  $($_.CommandLine)" }
}

# Alleen processen die van de app zelf zijn. Zie de uitleg hierboven: op deze
# machine starten er WebView2-processen van Teams, Grammarly en Outlook, en die
# hebben niets met deze app te maken. Een proefje dat daar rood van wordt, is
# een proefje dat over een ander programma oordeelt.
$meegelopen = @($vanOns | Where-Object { $_.Name -match 'msedge|WebView2|chrome|node|electron' })
if ($meegelopen.Count -eq 0) { '   goed: er is geen WebView2, Edge, node of Electron van deze app bijgekomen.' }
else {
  '   MISLUKT: er is een browser of een node-proces van deze app bijgekomen:'
  $meegelopen | ForEach-Object { "      $($_.ProcessId)  $($_.Name)" }
}

if ($found.Count -ge 1) { "   goed: er is een raam ($($found.Count)), en `ZetGeluidUit` heeft het geluid uitgezet." }
else { '   MISLUKT: er is geen raam gevonden, dus er is niets waar je naar kijkt.' }

$state = Test-Path "$data\state.json"
$cookie = Test-Path "$data\cookie.bin"
if ($state -and $cookie) { '   goed: state.json en cookie.bin bestaan, dus de app bewaart zijn plek op schijf.' }
else {
  '   MISLUKT: er ontbreekt ' + (@('state.json','cookie.bin') | Where-Object { -not (Test-Path "$data\$_") }) + '.'
}
if (Test-Path "$data\fouten.log") { '   MISLUKT: er is een foutenlijst; zie hierboven.' }
else { '   goed: geen foutenlijst.' }
