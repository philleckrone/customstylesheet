<#
.SYNOPSIS
  UI test of the list editors on a copy of the Bible21 fixture: the OccursUnder checklist
  ("Choose..." -> Copy stock list -> OK) and the Paratext color name drop-down. Saves a
  screenshot of the checklist to out\checklist.png. Exit code 0 = all checks passed.
#>
param([string]$ExePath = '')
$ErrorActionPreference = 'Stop'
if (-not $ExePath) { $ExePath = Join-Path $PSScriptRoot '..\CustomStyEditor\bin\Release\CustomStyEditor.exe' }
. (Join-Path $PSScriptRoot 'ui_common.ps1')
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -Namespace W -Name U -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h);
'@ -ErrorAction SilentlyContinue

$tmp = Join-Path $env:TEMP ('customsty_lists_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force (Join-Path $tmp 'Bible21') | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'fixtures\usfm.sty') $tmp
Copy-Item (Join-Path $PSScriptRoot 'fixtures\Bible21\*') (Join-Path $tmp 'Bible21')
$file = Join-Path $tmp 'Bible21\custom.sty'
$original = [IO.File]::ReadAllText($file)
$stockXt = ((Get-Content (Join-Path $PSScriptRoot 'fixtures\usfm.sty') | Select-String -Pattern '^\\Marker xt$' -Context 0,8).Context.PostContext |
    Where-Object { $_ -like '\OccursUnder *' }) -replace '^\\OccursUnder ', ''
$failures = 0
function Check([bool]$ok, [string]$what) {
    if ($ok) { Write-Host "ok   $what" } else { Write-Host "FAIL $what"; $script:failures++ }
}
function Shot($hwnd, [string]$path) {
    Add-Type -Namespace W -Name R -MemberDefinition @'
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
[DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out RECT r);
'@ -ErrorAction SilentlyContinue
    $r = New-Object W.R+RECT
    [W.R]::GetWindowRect($hwnd, [ref]$r) | Out-Null
    $bmp = New-Object System.Drawing.Bitmap ($r.Right - $r.Left), ($r.Bottom - $r.Top)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}

$proc = Start-Process -FilePath $ExePath -ArgumentList ('"' + $file + '"') -PassThru
try {
    $win = WaitForWindow $proc
    SelectGridRow $win 9     # \xt

    # OccursUnder: Choose... is the second "Choose" button (the first belongs to TextProperties).
    $buttons = @(FindByType $win ([System.Windows.Automation.ControlType]::Button) | Where-Object { $_.Current.Name -like 'Choose*' -and $_.Current.Name -notlike 'Choose color*' })
    Check ($buttons.Count -eq 2) 'two Choose buttons'
    $buttons[1].GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $dlg = $null
    for ($i = 0; $i -lt 20 -and -not $dlg; $i++) {
        $dlg = FindByName $script:AE::RootElement 'Where \xt may occur' ([System.Windows.Automation.ControlType]::Window)
        if (-not $dlg) { $dlg = FindByName $win 'Where \xt may occur' ([System.Windows.Automation.ControlType]::Window) }
        if (-not $dlg) { Start-Sleep -Milliseconds 300 }
    }
    Check ($dlg -ne $null) 'checklist dialog opened'
    $unknown = FindByName $dlg 'ef   (not in the stylesheet)' ([System.Windows.Automation.ControlType]::CheckBox)
    Check ($unknown -ne $null) 'value not in the stylesheet listed at the top'
    Start-Sleep -Milliseconds 500
    Shot ([IntPtr]$dlg.Current.NativeWindowHandle) (Join-Path $PSScriptRoot 'out\checklist.png')
    Invoke (FindByName $dlg 'Copy stock list' ([System.Windows.Automation.ControlType]::Button))
    Start-Sleep -Milliseconds 300
    Invoke (FindByName $dlg 'OK' ([System.Windows.Automation.ControlType]::Button))
    Start-Sleep -Milliseconds 800

    # Paratext color name for \xt: replaces its \Color line in place.
    $names = FindById $win 'Row_ColorNames'
    $names.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Milliseconds 400
    $item = FindByName $names 'text_blue' ([System.Windows.Automation.ControlType]::ListItem)
    if (-not $item) {
        $item = @(FindByType $names ([System.Windows.Automation.ControlType]::ListItem) | Where-Object { $_.Current.Name -like '*text_blue*' -or ($_.FindFirst($script:TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($script:AE::NameProperty, 'text_blue')))) })[0]
    }
    Check ($item -ne $null) 'text_blue in the color names'
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $names.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
    Start-Sleep -Milliseconds 500

    [W.U]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 300
    [System.Windows.Forms.SendKeys]::SendWait('^s')
    Start-Sleep -Milliseconds 800

    $expected = $original.Replace("\Color 1219071`r`n\OccursUnder ef f io1 ip p pi x li li1 li2 li3 li4 ili",
                                  "\ColorName text_blue`r`n\OccursUnder $stockXt")
    $actual = [IO.File]::ReadAllText($file)
    Check ($actual -ceq $expected) 'OccursUnder = stock list, color name replaces \Color in place'
    if ($actual -cne $expected) { Write-Host '--- actual tail:'; Write-Host $actual.Substring([Math]::Max(0, $actual.Length - 400)) }
}
finally {
    if (-not $proc.HasExited) { $proc.Kill() }
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
if ($failures -eq 0) { Write-Host 'ALL LIST CHECKS PASSED'; exit 0 } else { Write-Host "$failures CHECK(S) FAILED"; exit 1 }
