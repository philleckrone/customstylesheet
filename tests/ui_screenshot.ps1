<#
.SYNOPSIS
  Opens the editor on a custom.sty, optionally selects a row of the marker list and
  switches the language, and saves a PNG of the main window (for visual review).
  The file is copied to a temporary project folder first, so the fixture is never changed.
#>
param([string]$Fixture = 'Bible21', [string]$Out = '', [int]$Row = 0, [string]$Language = '',
      [string]$ExePath = '')
$ErrorActionPreference = 'Stop'
if (-not $ExePath) { $ExePath = Join-Path $PSScriptRoot '..\CustomStyEditor\bin\Release\CustomStyEditor.exe' }
if (-not $Out) { $Out = Join-Path $PSScriptRoot 'out\screenshot.png' }
New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null

# Temporary projects folder: usfm.sty beside a copy of the fixture project.
$tmp = Join-Path $env:TEMP ('customsty_ui_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force (Join-Path $tmp $Fixture) | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'fixtures\usfm.sty') $tmp
Copy-Item (Join-Path $PSScriptRoot ('fixtures\' + $Fixture + '\*')) (Join-Path $tmp $Fixture)
$file = Join-Path $tmp ($Fixture + '\custom.sty')

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Win32Shot {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
'@
. (Join-Path $PSScriptRoot 'ui_common.ps1')

$args2 = '"' + $file + '"'
if ($Language) { $args2 += ' --lang ' + $Language }
$proc = Start-Process -FilePath $ExePath -ArgumentList $args2 -PassThru
try {
    $win = WaitForWindow $proc
    if ($Row -ge 0) { SelectGridRow $win $Row }
    Start-Sleep -Seconds 1
    [Win32Shot]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 500
    $r = New-Object Win32Shot+RECT
    [Win32Shot]::GetWindowRect($proc.MainWindowHandle, [ref]$r) | Out-Null
    $bmp = New-Object System.Drawing.Bitmap ($r.Right - $r.Left), ($r.Bottom - $r.Top)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
    $bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Host "Saved $Out"
}
finally {
    if (-not $proc.HasExited) { $proc.Kill() }
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
