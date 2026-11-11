<#
.SYNOPSIS
  Starts the editor on a custom.sty that does not exist yet (as the Paratext plugin does
  for most projects), defines a new marker \zcust, removes the stock marker \pn, saves,
  and checks the exact file that was written. Exit code 0 = all checks passed.
#>
param([string]$ExePath = '')
$ErrorActionPreference = 'Stop'
if (-not $ExePath) { $ExePath = Join-Path $PSScriptRoot '..\CustomStyEditor\bin\Release\CustomStyEditor.exe' }
. (Join-Path $PSScriptRoot 'ui_common.ps1')
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Namespace W -Name U -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h);' -ErrorAction SilentlyContinue

$tmp = Join-Path $env:TEMP ('customsty_new_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force (Join-Path $tmp 'PDT15') | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'fixtures\usfm.sty') $tmp
Copy-Item (Join-Path $PSScriptRoot 'fixtures\PDT15\Settings.xml') (Join-Path $tmp 'PDT15')
$file = Join-Path $tmp 'PDT15\custom.sty'
$failures = 0
function Check([bool]$ok, [string]$what) {
    if ($ok) { Write-Host "ok   $what" } else { Write-Host "FAIL $what"; $script:failures++ }
}
function FindDialog($win, [string]$title) {
    for ($i = 0; $i -lt 20; $i++) {
        $d = FindByName $script:AE::RootElement $title ([System.Windows.Automation.ControlType]::Window)
        if (-not $d) { $d = FindByName $win $title ([System.Windows.Automation.ControlType]::Window) }
        if ($d) { return $d }
        Start-Sleep -Milliseconds 300
    }
    return $null
}
function SetText($element, [string]$text) {
    $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text)
}

$proc = Start-Process -FilePath $ExePath -ArgumentList ('"' + $file + '"') -PassThru
try {
    $win = WaitForWindow $proc
    Check (-not (Test-Path $file)) 'file not created before saving'
    Check ((GridRows $win).Count -eq 0) 'empty list'

    # New marker \zcust.
    Invoke (FindById $win 'NewMarkerButton')
    $dlg = FindDialog $win 'New Marker'
    Check ($dlg -ne $null) 'New Marker dialog opened'
    $edits = FindByType $dlg ([System.Windows.Automation.ControlType]::Edit)
    SetText $edits[0] 'zcust'
    SetText $edits[1] 'zcust - Custom test style'
    Start-Sleep -Milliseconds 300
    Invoke (FindByName $dlg 'OK' ([System.Windows.Automation.ControlType]::Button))
    Start-Sleep -Milliseconds 800

    # Stock marker \pn, then Remove from stylesheet.
    Invoke (FindById $win 'AddStockButton')
    $pick = FindDialog $win 'Add Stock Marker'
    SetText ((FindByType $pick ([System.Windows.Automation.ControlType]::Edit))[0]) 'pn...pn*'
    Start-Sleep -Milliseconds 400
    Invoke (FindByName $pick 'OK' ([System.Windows.Automation.ControlType]::Button))
    Start-Sleep -Milliseconds 800
    Invoke (FindById $win 'RemoveButton')
    Start-Sleep -Milliseconds 500

    [W.U]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 300
    [System.Windows.Forms.SendKeys]::SendWait('^s')
    Start-Sleep -Milliseconds 1000

    $expected = "\Marker zcust`r`n\Name zcust - Custom test style`r`n\StyleType Character`r`n\Endmarker zcust*`r`n" +
                "\TextType VerseText`r`n\TextProperties publishable vernacular`r`n`r`n\Marker pn -`r`n"
    Check (Test-Path $file) 'file created by Save'
    if (Test-Path $file) {
        $bytes = [IO.File]::ReadAllBytes($file)
        $actual = [Text.Encoding]::UTF8.GetString($bytes)
        Check ($actual -ceq $expected) 'file content'
        if ($actual -cne $expected) { Write-Host '--- actual:'; Write-Host $actual }
        Check (-not ($bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB)) 'UTF-8 without BOM'
        Copy-Item $file (Join-Path $PSScriptRoot 'out\newfile_custom.sty') -Force
    }
}
finally {
    if (-not $proc.HasExited) { $proc.Kill() }
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
if ($failures -eq 0) { Write-Host 'ALL NEW-FILE CHECKS PASSED'; exit 0 } else { Write-Host "$failures CHECK(S) FAILED"; exit 1 }
