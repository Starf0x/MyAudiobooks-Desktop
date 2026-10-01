param(
  [Parameter(Mandatory=$true)][int]$ProcessId,
  [string]$Action = 'dump',
  [string]$Id = '',
  [string]$Name = '',
  [string]$Value = '',
  [int]$MaxDepth = 6
)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName WindowsBase

$AE  = [System.Windows.Automation.AutomationElement]
$TS  = [System.Windows.Automation.TreeScope]
$CT  = [System.Windows.Automation.ControlType]
$walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker

function Get-Win {
  $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $ProcessId)
  $all = $AE::RootElement.FindAll($TS::Children, $cond)
  for ($i = 0; $i -lt $all.Count; $i++) {
    if ($all.Item($i).Current.ControlType -eq $CT::Window) { return $all.Item($i) }
  }
  return $null
}
function ById($root, $id) {
  $c = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
  return $root.FindFirst($TS::Descendants, $c)
}
function ByName($root, $name) {
  $c = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)
  return $root.FindFirst($TS::Descendants, $c)
}
function Size($r) { $w = $r.Width; $h = $r.Height; return "$([int]$w)x$([int]$h)" }

$win = Get-Win
if ($null -eq $win) { "GEEN VENSTER voor pid $ProcessId"; exit 1 }

switch ($Action) {
  'dump' {
    "venster '$($win.Current.Name)' $(Size $win.Current.BoundingRectangle)"
    $queue = New-Object System.Collections.Queue
    $queue.Enqueue(@($win, 0))
    while ($queue.Count -gt 0) {
      $pair = $queue.Dequeue(); $el = $pair[0]; $d = $pair[1]
      $c = $el.Current
      $line = ('  ' * $d) + "$($c.ControlType.ProgrammaticName.Replace('ControlType.',''))"
      if ($c.AutomationId) { $line += " id=$($c.AutomationId)" }
      if ($c.Name) { $line += " '$($c.Name)'" }
      $line += " " + (Size $c.BoundingRectangle)
      $line
      if ($d -ge $MaxDepth) { continue }
      $ch = $walker.GetFirstChild($el)
      while ($null -ne $ch) { $queue.Enqueue(@($ch, $d + 1)); $ch = $walker.GetNextSibling($ch) }
    }
  }
  'buttons' {
    $c = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::Button)
    $bs = $win.FindAll($TS::Descendants, $c)
    "knoppen: $($bs.Count)"
    for ($i = 0; $i -lt $bs.Count; $i++) {
      $b = $bs.Item($i).Current
      $r = $b.BoundingRectangle
      "  [$i] id=$($b.AutomationId) '$($b.Name)' $(Size $r) @$([int]$r.X),$([int]$r.Y)"
    }
  }
  'labels' {
    # De teksten met hun linkerrand, want een rij begint op een pixel en niet op
    # een gevoel. De 'buttons' hierboven geeft de knop, en een knop zegt niets
    # over waar het woord staat: die heeft een marge en een vulling.
    $c = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::Text)
    $ts = $win.FindAll($TS::Descendants, $c)
    "teksten: $($ts.Count)"
    for ($i = 0; $i -lt $ts.Count; $i++) {
      $t = $ts.Item($i).Current
      if (-not $t.Name) { continue }
      $r = $t.BoundingRectangle
      "  x=$([int]$r.X) y=$([int]$r.Y) h=$([int]$r.Height) w=$([int]$r.Width) '$($t.Name)'"
    }
  }
  'invoke' {
    $el = if ($Id) { ById $win $Id } else { ByName $win $Name }
    if ($null -eq $el) { "NIET GEVONDEN: id='$Id' name='$Name'"; exit 1 }
    $pat = $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    "klik '$($el.Current.Name)' (id=$($el.Current.AutomationId))"
    $pat.Invoke()
    Start-Sleep -Milliseconds 300
    "gedaan"
  }
  'type' {
    $el = if ($Id) { ById $win $Id } else { ByName $win $Name }
    if ($null -eq $el) { "NIET GEVONDEN: id='$Id' name='$Name'"; exit 1 }
    $pat = $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $pat.SetValue($Value)
    "waarde van '$($el.Current.Name)' nu: '$($el.GetCurrentPropertyValue($AE::ValueValueProperty))'"
  }
  'value' {
    $el = if ($Id) { ById $win $Id } else { ByName $win $Name }
    if ($null -eq $el) { "NIET GEVONDEN: id='$Id' name='$Name'"; exit 1 }
    "'$($el.Current.Name)' = '$($el.GetCurrentPropertyValue($AE::ValueValueProperty))'"
  }
  # Lees de tekst van het element met dít id.
  #
  # `texts` en `labels` geven alles, en dat is goed om te kijken. Om iets te
  # meten is het te breed: de eerste regel die op een klok lijkt is de titel van
  # een boek dat "0:00" in de naam heeft, of de notitie van een ander deel, of
  # de resterende tijd van een boek dat verderop in de lijst staat. Met een id
  # is er maar één element dat het kan zijn.
  'text' {
    $el = ById $win $Id
    if ($null -eq $el) { "NIET GEVONDEN: id='$Id'"; exit 1 }
    $r = $el.Current.BoundingRectangle
    "'$($el.Current.Name)' $(Size $r) @$([int]$r.X),$([int]$r.Y)"
  }
  # Alle elementen met dít id, één per regel. Voor rijen die er meerdig zijn,
  # zoals de notitie onder elk deel.
  'textsById' {
    $c = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $Id)
    $ts = $win.FindAll($TS::Descendants, $c)
    "stuks: $($ts.Count)"
    for ($i = 0; $i -lt $ts.Count; $i++) {
      $t = $ts.Item($i).Current
      $r = $t.BoundingRectangle
      "  [$i] '$($t.Name)' @(x=$([int]$r.X) y=$([int]$r.Y))"
    }
  }
  'invokeIndex' {
    $c = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::Button)
    $bs = $win.FindAll($TS::Descendants, $c)
    $b = $bs.Item([int]$Value)
    $pat = $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    "klik knop [$Value] id=$($b.Current.AutomationId) naam='$($b.Current.Name)' $(Size $b.Current.BoundingRectangle)"
    $pat.Invoke()
    Start-Sleep -Milliseconds 300
    "gedaan"
  }
  # Klik op de knop waarvan de naam dit stukje tekst bevat.
  #
  # Nodig voor knoppen waar een teken in de naam staat ("▶ Afspelen"): zo'n naam
  # verandert zodra de app een ander teken kiest, en dan werkt zo'n proefje
  # stilletjes niet meer meer.
  'invokeLike' {
    $c = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::Button)
    $bs = $win.FindAll($TS::Descendants, $c)
    for ($i = 0; $i -lt $bs.Count; $i++) {
      if ($bs.Item($i).Current.Name -like "*$Value*") {
        $b = $bs.Item($i)
        $pat = $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        "klik '$($b.Current.Name)' $(Size $b.Current.BoundingRectangle)"
        $pat.Invoke()
        Start-Sleep -Milliseconds 300
        "gedaan"
        exit 0
      }
    }
    "NIET GEVONDEN: geen knop waar '$Value' in de naam zit"
    exit 1
  }
  # Klik op de knop met déze naam, en niet op "de vijfde knop".
  #
  # Op nummer klikken is een tijdbom: elke keer dat de indeling verandert
  # schuift alles een plaats op, en dan klikt het proefje op iets anders zonder
  # dat het iets zegt. Zo gebeurde het: het proefje dat moest aantonen dat
  # afspelen werkte, klikte na een layoutwijziging op de genre-rij en meldde
  # braaf "0 mp3-verzoeken", alsof er niets aan de hand was. Op naam klikken
  # faalt luid.
  'invokeName' {
    $c = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $Value)
    $b = $win.FindFirst($TS::Descendants, $c)
    if ($null -eq $b) { "NIET GEVONDEN: knop met de naam '$Value'"; exit 1 }
    $pat = $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    "klik '$($b.Current.Name)' $(Size $b.Current.BoundingRectangle)"
    $pat.Invoke()
    Start-Sleep -Milliseconds 300
    "gedaan"
  }
  'texts' {
    $c = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::Text)
    $ts = $win.FindAll($TS::Descendants, $c)
    "teksten: $($ts.Count)"
    for ($i = 0; $i -lt $ts.Count; $i++) {
      $t = $ts.Item($i).Current
      if ($t.Name) { "  '$($t.Name)'" }
    }
  }
}
