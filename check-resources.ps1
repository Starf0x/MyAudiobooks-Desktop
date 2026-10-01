# Controleert dat elke FindResource in de code het soort haalt dat er staat.
#
# Er staan in dit project twee soorten resources naast elkaar: stijlen
# (`Text`, `Plain`, `Gold`, …) en kleuren (`Ink`, `InkFaint`, `Accent`, …). Ze
# hebben overlappende namen — `Faint` is een stijl, `InkFaint` de kleur — en een
# `(Brush)FindResource("Faint")` compileert gewoon en gooit pas een
# InvalidCastException als de regel wordt uitgevoerd. Dat kostte vijf runtime-
# fouten, waarvan er vier in de spelerbalk zaten en vier keer per seconde
# terugkwamen.
#
# Er is geen testproject, dus dit is de test: geen enkele bronregel mag een
# resource van het verkeerde soort nemen. Uitvoeren met PowerShell; hij stopt
# met een exitcode van 1 zodra er iets niet klopt.
$ErrorActionPreference = 'Stop'

# De stijlen uit App.xaml, en de penseelen. Beide uit dezelfde bron gelezen, want
# een lijst hier die niet meer klopt met App.xaml zou precies de fout zijn die
# dit script moet vinden.
$xaml = Get-Content (Join-Path $PSScriptRoot 'App.xaml') -Raw
$styles = [regex]::Matches($xaml, '<Style\s+x:Key="(\w+)"') | ForEach-Object { $_.Groups[1].Value }
$brushes = [regex]::Matches($xaml, '<SolidColorBrush\s+x:Key="(\w+)"') | ForEach-Object { $_.Groups[1].Value }
$fonts = [regex]::Matches($xaml, '<FontFamily\s+x:Key="(\w+)"') | ForEach-Object { $_.Groups[1].Value }

$kindOf = @{}
foreach ($k in $styles) { $kindOf[$k] = 'Style' }
foreach ($k in $brushes) { $kindOf[$k] = 'Brush' }
foreach ($k in $fonts) { $kindOf[$k] = 'FontFamily' }

if ($kindOf.Count -eq 0) { "FOUT: App.xaml gelezen maar er stonden geen resources in."; exit 1 }

$bad = @()
$checked = 0
foreach ($file in Get-ChildItem (Join-Path $PSScriptRoot '*.cs')) {
  $lines = Get-Content $file.FullName
  for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    foreach ($hit in [regex]::Matches($line, '\((Style|Brush|SolidColorBrush|FontFamily)\)\s*(Application\.Current\.)?FindResource\(([^)]*)\)')) {
      $wanted = $hit.Groups[1].Value
      if ($wanted -eq 'SolidColorBrush') { $wanted = 'Brush' }
      foreach ($key in [regex]::Matches($hit.Groups[3].Value, '"(\w+)"')) {
        $checked++
        $name = $key.Groups[1].Value
        if (-not $kindOf.ContainsKey($name)) {
          $bad += "$($file.Name):$($i + 1)  '$name' bestaat niet in App.xaml  ($($line.Trim()))"
        }
        elseif ($kindOf[$name] -ne $wanted) {
          $bad += "$($file.Name):$($i + 1)  '$name' is een $($kindOf[$name]) en wordt als $wanted gebruikt  ($($line.Trim()))"
        }
      }
    }
  }
}

"resources in App.xaml: $($styles.Count) stijlen, $($brushes.Count) penseelen, $($fonts.Count) lettertypen"
"aanroepen gecontroleerd: $checked"
if ($bad) {
  ""
  "FOUTEN:"
  $bad | ForEach-Object { "  $_" }
  exit 1
}
"alles klopt"
