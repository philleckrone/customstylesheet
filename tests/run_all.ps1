<#
.SYNOPSIS
  Builds the test tools and runs every check of the Custom Stylesheet Editor:
    1. round trip: every real custom.sty (and *.sty in the projects folder) loads and saves byte for byte;
    2. merge logic vs Paratext: base + custom.sty as the editor merges it equals what
       Paratext's own ScrStylesheet (ParatextData.dll, through oracle\Oracle.exe) reads;
    3. scripted edits, then Paratext's parser on the edited real files (no new errors);
    4. UI Automation tests (ui_roundtrip.ps1, ui_newfile.ps1, ui_lists.ps1) unless -NoUi.
  Needs Paratext 9 in C:\Program Files\Paratext 9 and the projects in -ProjectsDir.
#>
param([string]$ProjectsDir = 'C:\My Paratext 8 Projects', [switch]$NoUi, [string]$ExePath = '')
$ErrorActionPreference = 'Stop'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$here = $PSScriptRoot
$failed = @()

Push-Location (Join-Path $here 'oracle')
try { & $csc -nologo -out:Oracle.exe Oracle.cs; if ($LASTEXITCODE) { throw 'Oracle build failed' } } finally { Pop-Location }
Push-Location (Join-Path $here 'harness')
try { & $csc '@build.rsp'; if ($LASTEXITCODE) { throw 'Harness build failed' } } finally { Pop-Location }
$harness = Join-Path $here 'harness\Harness.exe'

Write-Host '== 1. Round trip'
$files = @(Get-ChildItem $ProjectsDir -Filter custom.sty -Recurse -Depth 1 | Where-Object { $_.Name -notlike '*.bak' } | ForEach-Object FullName)
$files += @(Get-ChildItem $ProjectsDir -Filter *.sty | ForEach-Object FullName)
$files += @(Get-ChildItem (Join-Path $here 'fixtures') -Filter *.sty -Recurse | ForEach-Object FullName)
& $harness roundtrip $files
if ($LASTEXITCODE) { $failed += 'roundtrip' }

Write-Host '== 2. Merge logic vs Paratext'
$out = & python (Join-Path $here 'compare.py')
$out | Select-Object -Last 15 | ForEach-Object { Write-Host $_ }
if (($out | Select-Object -Last 1) -ne 'fields differing: {}') { $failed += 'compare' }

Write-Host '== 3. Scripted edits'
& $harness edits | Select-Object -Last 3 | ForEach-Object { Write-Host $_ }
if ($LASTEXITCODE) { $failed += 'edits' }
$out = & python (Join-Path $here 'check_edits.py')
$out | ForEach-Object { Write-Host $_ }
if (($out | Select-Object -Last 1) -notmatch ', 0 failed$') { $failed += 'check_edits' }

if (-not $NoUi) {
    Write-Host '== 4. UI tests'
    $uiArgs = @()
    if ($ExePath) { $uiArgs = @('-ExePath', $ExePath) }
    foreach ($t in 'ui_roundtrip.ps1', 'ui_newfile.ps1', 'ui_lists.ps1') {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $here $t) @uiArgs
        if ($LASTEXITCODE) { $failed += $t }
    }
}

if ($failed.Count) { Write-Host ('FAILED: ' + ($failed -join ', ')); exit 1 }
Write-Host 'ALL TESTS PASSED'
