<#
.SYNOPSIS
  Drives the editor through UI Automation on a copy of the Bible21 fixture and checks the
  saved bytes: an edit inside an existing block stays in that block, a text value is
  committed when focus leaves the box, and a newly added stock marker goes at the end.
  Exit code 0 = all checks passed.
#>
param([string]$ExePath = '')
$ErrorActionPreference = 'Stop'
if (-not $ExePath) { $ExePath = Join-Path $PSScriptRoot '..\CustomStyEditor\bin\Release\CustomStyEditor.exe' }
. (Join-Path $PSScriptRoot 'ui_common.ps1')
Add-Type -AssemblyName System.Windows.Forms

$tmp = Join-Path $env:TEMP ('customsty_rt_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force (Join-Path $tmp 'Bible21') | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'fixtures\usfm.sty') $tmp
Copy-Item (Join-Path $PSScriptRoot 'fixtures\Bible21\*') (Join-Path $tmp 'Bible21')
$file = Join-Path $tmp 'Bible21\custom.sty'
$original = [IO.File]::ReadAllText($file)
$failures = 0
function Check([bool]$ok, [string]$what) {
    if ($ok) { Write-Host "ok   $what" } else { Write-Host "FAIL $what"; $script:failures++ }
}

function SelectComboItem($win, [string]$id, [int]$index) {
    $combo = FindById $win $id
    $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Milliseconds 300
    $items = FindByType $combo ([System.Windows.Automation.ControlType]::ListItem)
    $items[$index].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
    Start-Sleep -Milliseconds 400
}

function SaveFile($proc) {
    Add-Type -Namespace W -Name U -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h);' -ErrorAction SilentlyContinue
    [W.U]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 300
    [System.Windows.Forms.SendKeys]::SendWait('^s')
    Start-Sleep -Milliseconds 800
}

$proc = Start-Process -FilePath $ExePath -ArgumentList ('"' + $file + '"') -PassThru
try {
    $win = WaitForWindow $proc
    Check ((GridRows $win).Count -eq 10) 'ten markers listed'

    # 1. \fqa (row 1): Italic = Yes -> new line inside the fqa block.
    SelectGridRow $win 1
    SelectComboItem $win 'Row_Italic_Combo' 1
    SaveFile $proc
    $expected = $original.Replace("\color 11229501`r`n", "\color 11229501`r`n\Italic`r`n")
    Check ([IO.File]::ReadAllText($file) -ceq $expected) 'Italic added inside the \fqa block'

    # 2. FontSize typed into the box, committed when focus moves to the list.
    $box = FindById $win 'Row_FontSize_Box'
    $box.SetFocus()
    $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('10')
    (FindById $win 'MarkerGrid').SetFocus()
    Start-Sleep -Milliseconds 400
    SaveFile $proc
    $expected = $expected.Replace("\Italic`r`n", "\Italic`r`n\FontSize 10`r`n")
    Check ([IO.File]::ReadAllText($file) -ceq $expected) 'FontSize committed on focus change'

    # 3. Add stock marker \bd with Bold = No -> new block at the end of the file.
    Invoke (FindById $win 'AddStockButton')
    Start-Sleep -Milliseconds 800
    $dlg = $null
    for ($i = 0; $i -lt 20 -and -not $dlg; $i++) {
        $dlg = FindByName $script:AE::RootElement 'Add Stock Marker' ([System.Windows.Automation.ControlType]::Window)
        if (-not $dlg) { $dlg = FindByName $win 'Add Stock Marker' ([System.Windows.Automation.ControlType]::Window) }
        if (-not $dlg) { Start-Sleep -Milliseconds 300 }
    }
    Check ($dlg -ne $null) 'Add Stock Marker dialog opened'
    $filter = (FindByType $dlg ([System.Windows.Automation.ControlType]::Edit))[0]
    $filter.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('bd...bd*')
    Start-Sleep -Milliseconds 400
    Invoke (FindByName $dlg 'OK' ([System.Windows.Automation.ControlType]::Button))
    Start-Sleep -Milliseconds 800
    SelectComboItem $win 'Row_Bold_Combo' 2
    SaveFile $proc
    $expected = $expected + "`r`n`r`n\Marker bd`r`n\Bold -`r`n"
    $actual = [IO.File]::ReadAllText($file)
    Check ($actual -ceq $expected) 'new \bd block appended at the end'
    if ($actual -cne $expected) { Write-Host '--- actual tail:'; Write-Host $actual.Substring([Math]::Max(0, $actual.Length - 200)) }
    Check ((StatusText $win) -notmatch '^\*') 'status shows saved (no *)'
}
finally {
    if (-not $proc.HasExited) { $proc.Kill() }
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
if ($failures -eq 0) { Write-Host 'ALL UI ROUND-TRIP CHECKS PASSED'; exit 0 } else { Write-Host "$failures UI CHECK(S) FAILED"; exit 1 }
