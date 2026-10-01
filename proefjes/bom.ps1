# Zet een BOM op de proefjes, en zeg of ze allemaal nog gelden.
#
# Waarom dit nodig is: Windows PowerShell leest een `.ps1` zonder BOM in de
# ANSI-codepagina van de machine, in plaats van UTF-8. Op deze machine is dat
# cp1252, en daar is byte 0x94 een sluitend aanhalingsteken. Een em-dash `—` is
# in UTF-8 het drietal E2 80 94, en dat derde byte is dus een `"`. Gevolg: het
# proefje breekt af met "The string is missing the terminator" op een regel die
# er volkomen normaal uitziet, en de fout wijst naar de regel erna.
#
# Dat is geen theorie: het kostte een proefje (`geen-mp3-route.ps1`) dat om
# de een of andere reden hier terechtkwam.
#
# Dus: elk proefje krijgt een BOM, en daarna wordt elk proefje geparseerd. Dat
# is geen extra controle maar de enige manier om het te weten, want een
# syntactisch kapot proefje doet niets en zegt niets.
#
# En daarna komt de derde controle, op het oordeelwoord. `alle.ps1` beslist groen
# of rood door in de uitvoer te zoeken naar `MISLUKT`. Een proefje dat in plaats
# daarvan `MIJLUKT` schrijft — en dat gebeurde in drie proefjes, zeven keer — is
# een proefje dat altijd groen is. Dat is geen typfout maar een uitgeschakelde
# proef, dus hij wordt hier opgespoord voordat er op losse woorden wordt
# vertrouwd.
$ErrorActionPreference = 'Stop'
$uni = New-Object System.Text.UTF8Encoding($true)
$zin = New-Object System.Text.UTF8Encoding($false)

$kapot = 0
$fouteWoorden = 0
foreach ($f in Get-ChildItem (Join-Path $PSScriptRoot '*.ps1')) {
  $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
  $heeftBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
  if (-not $heeftBom) {
    $tekst = $zin.GetString($bytes)
    [System.IO.File]::WriteAllText($f.FullName, $tekst, $uni)
    "BOM toegevoegd: $($f.Name)"
  }

  $tokens = $null; $errors = $null
  [System.Management.Automation.Language.Parser]::ParseFile(
    $f.FullName, [ref]$tokens, [ref]$errors) | Out-Null
  if ($errors.Count -gt 0) {
    $kapot++
    "ONGELDIG: $($f.Name)"
    $errors | Select-Object -First 3 | ForEach-Object { "   regel $($_.Extent.StartLineNumber): $($_.Message)" }
  }

  # Drie bestanden worden niet op oordeelwoorden gecontroleerd, en die staan
  # hier bij naam bij: `bom.ps1`, `alle.ps1` en `proef-bewijst-zichzelf.ps1`. Ze
  # noemen het verkeerde woord om uit te leggen waarom het verboden is, en ze
  # bevatten het zoekpatroon zelf, en een bestand dat de regel bevat die het
  # naleeft is een bekende uitzondering op zijn eigen regel. De lijst staat hier
  # in plaats van in een commentaar, want een lijst die je niet ziet is een lijst
  # die je vergeet uit te breiden.
  #
  # Het derde bestand is er later bijgekomen en moest in deze lijst, want het
  # noemt het woord om te zeggen dat het proefje het hoort te geven. Een lijst
  # die je niet uitbreidt maakt een proefje rood op zijn eigen bestaan.
  if ($f.Name -in @('bom.ps1', 'alle.ps1', 'proef-bewijst-zichzelf.ps1')) { continue }

  $regels = Get-Content $f.FullName
  for ($i = 0; $i -lt $regels.Count; $i++) {
    # Alleen HOEKSTAATWOORDEN, en niet `\w*sluk\w*` met de `IgnoreCase`. Het
    # oordeel wordt altijd in hoofdletters geschreven, en alleen zo zijn
    # mislukte aanmelding en een proefje dat altijd groen is uit elkaar te
    # houden. De eerste versie van deze controle riep `hulp.ps1` regel 34
    # "mislukte" in een uitleg, en riep dat woord verkeerd.
    #
    # En het patroon zoekt op `LUK`, niet op `SLUKT`: een proefje dat
    # `MISLKT` schrijft is net zo stomp als een die `MIJLUKT` schrijft, en die
    # twee verschillen in een teken.
    foreach ($m in [regex]::Matches($regels[$i], '\b[A-Z]*LUK[A-Z]*\b')) {
      if ($m.Value -ne 'MISLUKT') {
        $fouteWoorden++
        "ONGELDIG: $($f.Name) regel $($i + 1) schrijft '$($m.Value)' in plaats van MISLUKT;"
        "   dit proefje kan nooit rood worden, en alle.ps1 zoekt op MISLUKT."
      }
    }
  }
}

if ($fouteWoorden -gt 0) { "$fouteWoorden regel(s) met een verkeerd oordeelwoord"; exit 1 }
if ($kapot -gt 0) { "$kapot proefje(s) zijn geen geldige PowerShell"; exit 1 }
"alle proefjes zijn geldige PowerShell, en hun oordeelwoorden zijn MISLUKT"
