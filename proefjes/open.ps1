param([Parameter(Mandatory=$true)][int]$ProcessId, [string]$Title = '')
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName WindowsBase

$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]
$up = [System.Windows.Automation.TreeWalker]::ControlViewWalker

$cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $ProcessId)
$all = $AE::RootElement.FindAll($TS::Children, $cond)
$win = $null
for ($i = 0; $i -lt $all.Count; $i++) { if ($all.Item($i).Current.ControlType -eq $CT::Window) { $win = $all.Item($i); break } }
if ($null -eq $win) { "GEEN VENSTER"; exit 1 }

# The card has no name of its own - it is a button whose content is a picture and
# a title, and that is not something UI Automation can read. So: find the text of
# the title, then walk up to the button that holds it.
$texts = $win.FindAll($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::Text)))
"titels op scherm:"
$target = $null
for ($i = 0; $i -lt $texts.Count; $i++) {
  $t = $texts.Item($i)
  if ($t.Current.Name) { "  '$($t.Current.Name)'" }
  if ($Title -and $t.Current.Name -eq $Title) { $target = $t }
}
if (-not $Title) { exit 0 }
if ($null -eq $target) { "TITEL NIET GEVONDEN: $Title"; exit 1 }

$btn = $target
while ($null -ne $btn -and $btn.Current.ControlType -ne $CT::Button) { $btn = $up.GetParent($btn) }
if ($null -eq $btn) { "GEEN KNOP BOVEN DE TITEL"; exit 1 }
"klik op de kaart met '$Title'  ($(Size $btn.Current.BoundingRectangle))"
$btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
"gedaan"
