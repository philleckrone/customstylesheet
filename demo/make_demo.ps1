<#
.SYNOPSIS
  Makes a narrated demo video of Custom Stylesheet Editor (MP4, 1920x1080), following
  tools\demo_video\Demo_Video_Standard.md.
  1. Starts CustomStyEditor.exe on a copy of PDT15's custom.sty and drives it through UI
     Automation patterns (Value, Invoke, SelectionItem, ExpandCollapse, Scroll, Transform; WPF, so
     there are no child windows to post messages to). The mouse is never moved. The window is
     captured after each step.
  2. Draws each frame: a headline, the window with orange boxes round the controls used and a
     pointer, and the narration as subtitles.
  3. Speaks the narration (Azure AI Speech, see azure_tts.ps1) and joins the steps with ffmpeg.
  The text is in narration.json (UTF-8). This file stays ASCII.

.EXAMPLE
  .\make_demo.ps1 -Lang en -NoVoice
#>
param(
    [string]$Lang = 'en',
    [string]$Work = '',
    [string]$Project = 'C:\ClaudeCode Projects\TestProject\PDT15',
    # Where the demo copy of the project goes while the window is captured (see below).
    [string]$DemoRoot = 'C:\My Paratext 9 Projects',
    # The program as it is shipped in the plugin zip.
    [string]$Exe = '',
    # The base stylesheet put beside the demo project (Paratext keeps usfm.sty in the projects folder).
    [string]$UsfmSty = 'C:\ClaudeCode Projects\Custom_Stylesheet_Editor\tests\fixtures\usfm.sty',
    # How much wider the marker list is made (the splitter, in device-independent pixels), and how
    # far the property panel is scrolled (percent) so the color name list opens inside the window.
    [int]$ListWiden = 90,
    [double]$ColorScroll = 35,
    # An Azure voice (a name ending in 'Neural', e.g. en-US-JennyNeural; key and region from
    # AZURE_SPEECH_KEY / AZURE_SPEECH_REGION, see azure_tts.ps1) or a Windows voice (System.Speech).
    [string]$Voice = '',
    [int]$Rate = -1,                 # Windows voices: -10..10
    [string]$AzureRate = '0%',       # Azure voices: e.g. '-5%'
    # ffmpeg.exe to use; by default a newer GPL build in C:\ffmpeg-*, else the one on PATH.
    [string]$FFmpeg = '',
    # Use the screenshots of the last capture in $Work (no window is driven): for a voiced build or
    # a narration change after a -NoVoice test. The capture needs about 40 seconds without typing.
    [switch]$Reuse,
    # Only capture the window (then build with -Reuse): several languages can be captured in one
    # quiet-keyboard moment.
    [switch]$CaptureOnly,
    # Zoom into the boxed controls in steps without cues (written as ..._zoom.mp4).
    [switch]$Zoom,
    # No narration: each step stays up as long as its subtitle takes to read (for testing).
    [switch]$NoVoice,
    # Background music (looped, kept soft; lowered further while the voice speaks). '' = none.
    [string]$Music = 'C:\Users\Pleckrone\Dropbox\Camtasia work\Background Music Files\Aetheric - Coconut Kind of Love (freetouse.com).mp3',
    [double]$MusicVolume = 0.30,        # the music on its own, at the start (the standard)
    [double]$MusicUnderVoice = 0.025,    # from the narration on (the standard)
    [double]$CoverIntro = 3.5,          # seconds of music alone on the cover before the narration
    # The cover's second line, under the plugin's name (narration.json, step "title").
    [string]$Series = 'Plugins by Phil',
    # Dissolve between steps, and dip to black round the slides (seconds). This window has no
    # count line, so steps always dissolve (the standard, section 6); $Wipe is kept for that rule.
    [double]$Dissolve = 1.0,
    [double]$Wipe = 2.0,
    [double]$Dip = 1.4
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
if (-not $Exe) { $Exe = Join-Path (Split-Path $here) 'plugin\CustomStyEditor\CustomStyEditor.exe' }
if (-not $Work) { $Work = Join-Path $env:TEMP "customsty_demo_$Lang" }
if (-not $Voice) { $Voice = @{ 'en' = 'en-US-JennyNeural'; 'es' = 'es-MX-JorgeNeural'; 'fr' = 'fr-FR-RemyMultilingualNeural' }[$Lang] }   # the standard's voices
$azure = $Voice -match 'Neural$'
$musicOn = $Music -and (Test-Path -LiteralPath $Music)
if ($Music -and -not $musicOn) { "music file not found, none used: $Music" }
$es = $Lang -like 'es*'; $isFr = $Lang -like 'fr*'
# The window's (and the slides') text in the video's language: English, Spanish, French
# (the editor's own words are in CustomStyEditor\Resources\Strings.<lang>.xaml).
function T($en, $sp, $frText) { if ($es) { $sp } elseif ($isFr -and $null -ne $frText) { $frText } else { $en } }

$narr = Get-Content -LiteralPath (Join-Path $here 'narration.json') -Raw -Encoding UTF8 | ConvertFrom-Json
function Text($id) {
    $t = ($narr.steps | Where-Object { $_.id -eq $id } | Select-Object -First 1).$Lang
    if (-not $t) { throw "narration.json has no '$Lang' text for step $id" }
    $t
}
# Box cues in the narration (the standard, section 8). "{1}" before a phrase shows box 1 (the
# step's boxes, in order) from there on, "{0,2}" boxes 0 and 2, "{}" none; the pointer goes on the
# first box named. Before the first cue no box is shown. A text without cues shows all its boxes
# the whole time. Cues are left out of the subtitles and the voice.
$cueRx = '\{\s*(\d+(\s*,\s*\d+)*)?\s*\}'
function Clean($raw) { if ($raw) { [regex]::Replace($raw, $cueRx, '') } else { $raw } }
# The parts of a step: @{ Pos (in the clean text); Show (box numbers) }, or none without cues.
function Cues($raw) {
    $clean = ''; $marks = @(); $last = 0
    if (-not $raw) { return , @() }
    foreach ($m in [regex]::Matches($raw, $cueRx)) {
        $clean += $raw.Substring($last, $m.Index - $last)
        $show = @()
        if ($m.Groups[1].Success) { $show = @($m.Groups[1].Value -split '\s*,\s*' | ForEach-Object { [int]$_ }) }
        $marks += [pscustomobject]@{ Pos = $clean.Length; Show = $show }
        $last = $m.Index + $m.Length
    }
    if ($marks.Count -and $marks[0].Pos -gt 0) { $marks = @([pscustomobject]@{ Pos = 0; Show = @() }) + $marks }
    , $marks
}

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms, System.Speech
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class DmWin {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int L, T, R, B; }
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
    /// <summary>Per-monitor aware, so window rectangles, DWM frames and screen copies all use
    /// physical pixels on monitors with different scales.</summary>
    public static void BeAware() { try { if (SetProcessDpiAwarenessContext((IntPtr)(-4))) return; } catch { } SetProcessDPIAware(); }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out Rect r);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out Rect r, int size);
    /// <summary>The visible frame (without the invisible resize borders), in screen pixels.</summary>
    public static Rect Frame(IntPtr h) {
        Rect r;
        if (DwmGetWindowAttribute(h, 9, out r, Marshal.SizeOf(typeof(Rect))) != 0) GetWindowRect(h, out r);
        return r;
    }
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, int flags);
    /// <summary>Keeps a window above all others (no move, no size), so screen copies show it.</summary>
    public static void TopMost(IntPtr h) { SetWindowPos(h, (IntPtr)(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); }
    /// <summary>Moves a window (no size change, no activation).</summary>
    public static void MoveTo(IntPtr h, int x, int y) { SetWindowPos(h, IntPtr.Zero, x, y, 0, 0, 0x0001 | 0x0004 | 0x0010); }
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct StartupInfo {
        public int cb; public string lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }
    [StructLayout(LayoutKind.Sequential)] struct ProcessInfo { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateProcess(string app, System.Text.StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, int flags, IntPtr env, string dir, ref StartupInfo si, out ProcessInfo pi);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    /// <summary>Starts a program whose first window is shown without being activated
    /// (SW_SHOWNOACTIVATE), so it does not take the keyboard from whatever the user is typing in.</summary>
    public static int StartNoActivate(string exe, string args) {
        var si = new StartupInfo(); si.cb = Marshal.SizeOf(typeof(StartupInfo));
        si.dwFlags = 1; si.wShowWindow = 4;
        ProcessInfo pi;
        if (!CreateProcess(exe, new System.Text.StringBuilder("\"" + exe + "\" " + args), IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, null, ref si, out pi))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        CloseHandle(pi.hThread); CloseHandle(pi.hProcess);
        return pi.dwProcessId;
    }
}
'@
[DmWin]::BeAware()
$AE = [Windows.Automation.AutomationElement]
$Scope = [Windows.Automation.TreeScope]
$CT = [Windows.Automation.ControlType]
$root = $AE::RootElement

# ------------------------------------------------------------ the demo project
$shots = Join-Path $Work 'shots'
$frames = Join-Path $Work 'frames'
$stepsFile = Join-Path $Work 'steps.xml'
if ($Reuse) {
    if (-not (Test-Path -LiteralPath $stepsFile)) { throw "-Reuse: no capture in $Work (run once without -Reuse)" }
    $script:steps = @(Import-Clixml -LiteralPath $stepsFile)
    if (Test-Path $frames) { Remove-Item -Recurse -Force $frames }
    New-Item -ItemType Directory -Force $frames | Out-Null
    Get-ChildItem -LiteralPath $Work -File | Where-Object { $_.Name -ne 'steps.xml' } | Remove-Item -Force
    "reusing the capture of $((Get-Item -LiteralPath $stepsFile).LastWriteTime): $(($script:steps | ForEach-Object { $_.Id }) -join ' ')"
}
else {
if (Test-Path $Work) { Remove-Item -Recurse -Force $Work }
New-Item -ItemType Directory -Force $shots, $frames | Out-Null
"capturing: please do not type for about 40 seconds (filling a box makes the editor window active)" | Out-Host
# The project bar and the status bar show full paths, so the copy is put where a user's project
# would be, $DemoRoot\PDT15, but only when that folder does not exist yet (a real project is never
# touched); it is removed again after the capture. Otherwise it stays in $Work. The base
# stylesheet, usfm.sty, goes beside it (in the projects folder, as Paratext keeps it) only when
# this script made $DemoRoot; otherwise the editor finds Paratext's own projects folder.
$script:madeRoot = $false; $script:madeProj = $false; $script:madeSty = $false
$proj = Join-Path $DemoRoot 'PDT15'
if (-not (Test-Path -LiteralPath $proj)) {
    try {
        if (-not (Test-Path -LiteralPath $DemoRoot)) { New-Item -ItemType Directory $DemoRoot | Out-Null; $script:madeRoot = $true }
        New-Item -ItemType Directory $proj | Out-Null; $script:madeProj = $true
    }
    catch { "cannot make $proj ($($_.Exception.Message)): using the work folder"; $proj = Join-Path $Work 'PDT15' }
}
else { "$proj exists: using the work folder instead"; $proj = Join-Path $Work 'PDT15' }
New-Item -ItemType Directory -Force $proj | Out-Null
$projectsDir = Split-Path $proj
$baseSty = Join-Path $projectsDir 'usfm.sty'
if (-not (Test-Path -LiteralPath $baseSty) -and ($script:madeRoot -or -not $script:madeProj)) {
    Copy-Item -LiteralPath $UsfmSty -Destination $baseSty; $script:madeSty = $true
}
Copy-Item -LiteralPath (Join-Path $Project 'custom.sty'), (Join-Path $Project 'Settings.xml') -Destination $proj
$styFile = Join-Path $proj 'custom.sty'
function RemoveDemoProject {
    if ($script:madeSty) { Remove-Item -LiteralPath $baseSty -Force -ErrorAction SilentlyContinue; $script:madeSty = $false }
    if ($script:madeProj) {
        Get-ChildItem -LiteralPath $proj -File | Remove-Item -Force
        Remove-Item -LiteralPath $proj -Force; $script:madeProj = $false
    }
    if ($script:madeRoot) { Remove-Item -LiteralPath $DemoRoot -Force; $script:madeRoot = $false }
}

# ------------------------------------------------------------ driving the window
$script:proc = $null
$script:win = $null
trap {
    if ($script:proc -and -not $script:proc.HasExited) { $script:proc.Kill(); $script:proc.WaitForExit(5000) | Out-Null }
    RemoveDemoProject
    throw $_
}
function Launch {
    # As the plugin starts it: the project's custom.sty and Paratext's interface language.
    # Shown without being activated: keys typed elsewhere meanwhile must not reach its boxes.
    $script:proc = Get-Process -Id ([DmWin]::StartNoActivate($Exe, "`"$styFile`" --lang $Lang"))
    $byProcess = New-Object Windows.Automation.PropertyCondition ($AE::ProcessIdProperty, $script:proc.Id)
    for ($i = 0; $i -lt 100 -and -not $script:win; $i++) {
        Start-Sleep -Milliseconds 200
        $script:win = $root.FindFirst($Scope::Children, $byProcess)
    }
    if (-not $script:win) { throw 'window did not appear' }
    $h = [IntPtr]$script:win.Current.NativeWindowHandle
    # On a screen without the mouse cursor: hover effects would show in the pictures.
    $cursor = [Windows.Forms.Cursor]::Position
    $screen = [Windows.Forms.Screen]::AllScreens | Where-Object { -not $_.Bounds.Contains($cursor) } | Select-Object -First 1
    if ($screen) { [DmWin]::MoveTo($h, $screen.WorkingArea.X + 20, $screen.WorkingArea.Y + 20) }
    else { 'only one screen: park the mouse cursor away from the window' | Out-Host }
    [DmWin]::TopMost($h)
    Start-Sleep -Milliseconds 2500
    "editor window active: $([DmWin]::GetForegroundWindow() -eq $h)" | Out-Host
}
function All { @($script:win.FindAll($Scope::Descendants, [Windows.Automation.Condition]::TrueCondition)) }
function ById($id, $under = $null) {
    if (-not $under) { $under = $script:win }
    $c = New-Object Windows.Automation.PropertyCondition ($AE::AutomationIdProperty, $id)
    $el = $under.FindFirst($Scope::Descendants, $c)
    if (-not $el) { throw "control $id not found" }
    $el
}
function OfType($under, $type) {
    @($under.FindAll($Scope::Descendants, (New-Object Windows.Automation.PropertyCondition ($AE::ControlTypeProperty, $type))))
}
# A dialog of the editor: a window of its process that is not the main window.
function Dialog {
    $byProcess = New-Object Windows.Automation.PropertyCondition ($AE::ProcessIdProperty, $script:proc.Id)
    $isWin = New-Object Windows.Automation.PropertyCondition ($AE::ControlTypeProperty, $CT::Window)
    $main = $script:win.Current.NativeWindowHandle
    for ($i = 0; $i -lt 30; $i++) {
        $d = @($root.FindAll($Scope::Descendants, (New-Object Windows.Automation.AndCondition $byProcess, $isWin))) |
            Where-Object { $_.Current.NativeWindowHandle -ne $main } | Select-Object -First 1
        if ($d) { Start-Sleep -Milliseconds 600; return $d }
        Start-Sleep -Milliseconds 200
    }
    throw 'the dialog did not open'
}
function SetValue($el, $text) {
    $el.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($text)
    Start-Sleep -Milliseconds 700
}
function ValueOf($el) { $el.GetCurrentPropertyValue([Windows.Automation.ValuePattern]::ValueProperty) }
function Invoke($el) {
    $el.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 900
}
function Expand($el) {
    $el.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Milliseconds 900
}
function Collapse($el) {
    $el.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
    Start-Sleep -Milliseconds 500
}
function SelectEl($el) {
    $el.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 700
}
# The items of a combo box's list (open it first), and the one chosen.
function ListItems($combo) { OfType $combo $CT::ListItem }
function Chosen($items) {
    $items | Where-Object { $_.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected } | Select-Object -First 1
}
# Chooses an item of a combo box (by its index, or the first whose text matches), then shows the
# list open again with that item highlighted, for the picture. Returns @{ Items; Chosen }.
function ChooseOpen($combo, $match) {
    Expand $combo
    $items = ListItems $combo
    $pick = if ($match -is [int]) { $items[$match] } else {
        $items | Where-Object { $_.Current.Name -like $match -or ($_.FindFirst($Scope::Descendants, (New-Object Windows.Automation.PropertyCondition ($AE::NameProperty, ($match -replace '\*', ''))))) } | Select-Object -First 1
    }
    if (-not $pick) { throw "no item '$match' in the list" }
    SelectEl $pick
    try { Collapse $combo } catch { }
    Expand $combo
    $items = @(ListItems $combo | Where-Object { -not $_.Current.IsOffscreen -and -not $_.Current.BoundingRectangle.IsEmpty })
    @{ Items = $items; Chosen = (Chosen $items) }
}
# The marker list (DataGrid): rows top to bottom, a row's cells left to right.
function Rows {
    $c = New-Object Windows.Automation.PropertyCondition ($AE::ClassNameProperty, 'DataGridRow')
    @((ById 'MarkerGrid').FindAll($Scope::Descendants, $c)) | Sort-Object { $_.Current.BoundingRectangle.Y }
}
function Cells($row) {
    $c = New-Object Windows.Automation.PropertyCondition ($AE::ClassNameProperty, 'DataGridCell')
    @($row.FindAll($Scope::Children, $c)) | Sort-Object { $_.Current.BoundingRectangle.X }
}
function RowOf($marker) {
    $r = Rows | Where-Object { $cs = Cells $_; $cs.Count -and $cs[0].Current.Name -eq "\$marker" } | Select-Object -First 1
    if (-not $r) { throw "no row for \$marker" }
    $r
}
# A column of the list: its header plus the cells of the rows.
function ColumnEls($i) {
    $c = New-Object Windows.Automation.PropertyCondition ($AE::ClassNameProperty, 'DataGridColumnHeader')
    $heads = @((ById 'MarkerGrid').FindAll($Scope::Descendants, $c)) | Where-Object { -not $_.Current.BoundingRectangle.IsEmpty } | Sort-Object { $_.Current.BoundingRectangle.X }
    $els = @($heads[$i])
    foreach ($r in Rows) { $els += (Cells $r)[$i] }
    $els
}
# The stock value shown at the right of a property row ("usfm.sty: yes"): the text on the same line.
function StockOf($keyword) {
    $ref = (ById "Row_${keyword}_Enabled").Current.BoundingRectangle
    $mid = $ref.Y + $ref.Height / 2
    # "usfm.sty: yes", French "usfm.sty : oui"
    OfType $script:win $CT::Text | Where-Object { $_.Current.Name -match '\.sty\s?: ' } |
        Where-Object { $b = $_.Current.BoundingRectangle; -not $b.IsEmpty -and [Math]::Abs($b.Y + $b.Height / 2 - $mid) -lt 8 } | Select-Object -First 1
}
# The keyword label of a property row ("\Bold").
function KeywordOf($keyword) {
    OfType $script:win $CT::Text | Where-Object { $_.Current.Name -eq "\$keyword" } | Select-Object -First 1
}
# The scroll pattern of the property panel (the scroll viewer round the groups).
function PanelScroll {
    $ref = (ById 'Row_Bold_Combo').Current.BoundingRectangle
    $c = New-Object Windows.Automation.PropertyCondition ($AE::ClassNameProperty, 'ScrollViewer')
    $sv = @($script:win.FindAll($Scope::Descendants, $c)) | Where-Object { $_.Current.BoundingRectangle.Contains($ref.X + 2, $ref.Y + 2) } | Select-Object -First 1
    if (-not $sv) { throw 'property panel scroll viewer not found' }
    $sv.GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern)
}

# ------------------------------------------------------------ capturing a step
$script:steps = @()
# The window as it is on the screen (open lists, menus and dialogs included), in physical pixels.
function Capture([IntPtr]$h) {
    $f = [DmWin]::Frame($h)
    $bmp = New-Object Drawing.Bitmap ($f.R - $f.L), ($f.B - $f.T)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($f.L, $f.T, 0, 0, $bmp.Size)
    $g.Dispose()
    @{ Bmp = $bmp; Rect = $f }
}
# A box round some elements, in the capture's pixels (UI Automation gives physical pixels here).
function Box($els, $f) {
    $x1 = 1e9; $y1 = 1e9; $x2 = -1e9; $y2 = -1e9
    foreach ($e in $els) {
        if (-not $e) { continue }
        $b = $e.Current.BoundingRectangle
        if ($b.IsEmpty) { continue }
        $x1 = [Math]::Min($x1, $b.X); $y1 = [Math]::Min($y1, $b.Y)
        $x2 = [Math]::Max($x2, $b.X + $b.Width); $y2 = [Math]::Max($y2, $b.Y + $b.Height)
    }
    if ($x2 -lt $x1) { return $null }
    , @([int]($x1 - $f.L), [int]($y1 - $f.T), [int]($x2 - $x1), [int]($y2 - $y1))
}
# $groups: a list of element lists, one orange box each. $pointer: index of the box to point at, or -1.
# $pointEl: an element to put the pointer on instead (e.g. the chosen item of an open list).
function Step($id, $groups, $pointer = -1, $pointEl = $null) {
    Start-Sleep -Milliseconds 400
    $main = Capture ([IntPtr]$script:win.Current.NativeWindowHandle)
    $boxes = @()
    foreach ($grp in $groups) {
        $b = Box @($grp) $main.Rect
        if (-not $b) { throw "step $id : a box has no element on screen" }
        $boxes += , $b
    }
    $pointAt = $null
    if ($pointEl) { $pointAt = Box @($pointEl) $main.Rect }
    $file = Join-Path $shots "$id.png"
    $main.Bmp.Save($file, [Drawing.Imaging.ImageFormat]::Png)
    $main.Bmp.Dispose()
    # Rows = -1: no count line, so the joins between steps are always dissolves.
    $script:steps += [pscustomobject]@{ Id = $id; Shot = $file; Boxes = $boxes; Pointer = [int]$pointer; Dialog = $null; Rows = -1; PointAt = $pointAt }
    "captured $id$(if ([DmWin]::GetForegroundWindow() -eq [IntPtr]$script:win.Current.NativeWindowHandle) { ' (the editor window is active: keys typed now would reach it)' })"
}

# ------------------------------------------------------------ the demo
$stockMarker = 'add'                  # the stock marker customized: bold off, Paratext color text_blue
$colorName = 'text_blue'
$newMarker = 'zcheck'                 # the new marker
$newName = T 'zcheck - Text to check' 'zcheck - Texto por revisar' "zcheck - Texte $([char]0xE0) v$([char]0xE9)rifier"

Launch
# The marker list a little wider, so its Status column is not cut (the splitter, by UI Automation).
try {
    $c = New-Object Windows.Automation.PropertyCondition ($AE::ClassNameProperty, 'GridSplitter')
    $split = @($script:win.FindAll($Scope::Descendants, $c)) | Sort-Object { $_.Current.BoundingRectangle.Height } -Descending | Select-Object -First 1
    $split.GetCurrentPattern([Windows.Automation.TransformPattern]::Pattern).Move($ListWiden, 0)
    Start-Sleep -Milliseconds 600
}
catch { "the marker list could not be widened: $($_.Exception.Message)" }

$grid = ById 'MarkerGrid'
Step 's01_open' @(@((ById 'ProjectHeaderText'), (ById 'BaseSheetText')), @((ColumnEls 0) + (ColumnEls 1) + (ColumnEls 2) + (ColumnEls 3)), @(ColumnEls 4)) -1

# Add stock marker...: the picker, with the list narrowed to the marker.
$addStock = ById 'AddStockButton'
Invoke $addStock
$pick = Dialog
$filter = (OfType $pick $CT::Edit)[0]
SetValue $filter $stockMarker
$item = OfType $pick $CT::ListItem | Where-Object { $_.Current.Name -like "\$stockMarker *" } | Select-Object -First 1
if (-not $item) { throw "\$stockMarker is not in the stock marker list" }
SelectEl $item
$ok = OfType $pick $CT::Button | Where-Object { $_.Current.Name -eq (T 'OK' 'Aceptar' 'OK') } | Select-Object -First 1
Step 's02_addstock' @(@($addStock), @($filter), @($item), @($ok)) 0
Invoke $ok
Start-Sleep -Milliseconds 600
if ((RowOf $stockMarker).GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected -ne $true) { SelectEl (RowOf $stockMarker) }

# Bold: the list open with "No (\Bold -)" chosen; the stock values at the right.
$bold = ById 'Row_Bold_Combo'
$open = ChooseOpen $bold 2
Step 's03_bold' @(@((StockOf 'Bold'), (StockOf 'Italic')), (@((KeywordOf 'Bold'), $bold) + $open.Items)) -1 $open.Chosen
Collapse $bold

# Color: the property panel scrolled so the Paratext color name list opens inside the window.
$scroll = PanelScroll
$scroll.SetScrollPercent([Windows.Automation.ScrollPattern]::NoScroll, $ColorScroll)
Start-Sleep -Milliseconds 600
$names = ById 'Row_ColorNames'
$open = ChooseOpen $names $colorName
$nameLabel = OfType $script:win $CT::Text | Where-Object { $_.Current.Name -eq (T 'Paratext color name:' 'Nombre de color de Paratext:' "Nom de couleur Paratext :") } | Select-Object -First 1
# "Choose color..." (an exact color): the button just above the color name list.
$nr = $names.Current.BoundingRectangle
$pickColor = OfType $script:win $CT::Button | Where-Object { $b = $_.Current.BoundingRectangle; -not $b.IsEmpty -and $b.Bottom -le $nr.Top + 2 -and $b.Bottom -gt $nr.Top - 45 -and $b.Left -lt $nr.Right } | Select-Object -First 1
Step 's04_color' @((@($nameLabel, $names) + $open.Items), @($pickColor)) -1 $open.Chosen
Collapse $names
$scroll.SetScrollPercent([Windows.Automation.ScrollPattern]::NoScroll, 0)
Start-Sleep -Milliseconds 600

# The result: the list's Changes column, the preview and the lines of custom.sty.
$previewTitle = OfType $script:win $CT::Text | Where-Object { $_.Current.Name -eq (T 'Preview (approximate)' 'Vista previa (aproximada)' "Aper$([char]0xE7)u (approximatif)") } | Select-Object -First 1
$linesTitle = OfType $script:win $CT::Text | Where-Object { $_.Current.Name -eq (T 'Lines in custom.sty' "L$([char]0xED)neas en custom.sty" 'Lignes dans custom.sty') } | Select-Object -First 1
Step 's05_result' @(@(RowOf $stockMarker), @($previewTitle, (ById 'PreviewViewer')), @($linesTitle, (ById 'FileTextBox'))) -1

# New marker...: the dialog filled in.
$newBtn = ById 'NewMarkerButton'
Invoke $newBtn
$dlg = Dialog
$edits = @(OfType $dlg $CT::Edit | Sort-Object { $_.Current.BoundingRectangle.Y })
SetValue $edits[0] $newMarker
SetValue $edits[1] $newName
$combos = @(OfType $dlg $CT::ComboBox | Sort-Object { $_.Current.BoundingRectangle.Y })
$ok = OfType $dlg $CT::Button | Where-Object { $_.Current.Name -eq (T 'OK' 'Aceptar' 'OK') } | Select-Object -First 1
Step 's06_new' @(@($newBtn), @($edits[0], $edits[1]), @($combos[0]), @($ok)) 0
Invoke $ok
Start-Sleep -Milliseconds 600
Step 's07_added' @(@(RowOf $newMarker), @(ById 'RemoveButton'), @(ById 'DeleteButton')) 0

# File > Save, with the File menu open.
$fileMenu = OfType $script:win $CT::MenuItem | Where-Object { $_.Current.Name -eq (T 'File' 'Archivo' 'Fichier') } | Select-Object -First 1
Expand $fileMenu
$saveItem = @($fileMenu.FindAll($Scope::Descendants, (New-Object Windows.Automation.PropertyCondition ($AE::ControlTypeProperty, $CT::MenuItem)))) |
    Where-Object { $_.Current.Name -eq (T 'Save' 'Guardar' 'Enregistrer') } | Select-Object -First 1
if (-not $saveItem) { throw 'File > Save not found' }
Step 's08_save' @(, @($fileMenu, $saveItem)) -1 $saveItem
Invoke $saveItem
Start-Sleep -Milliseconds 800
# The saved file must be the original plus the two new entries: anything else means keys typed
# elsewhere reached the window (it becomes active when a box is filled through UI Automation).
$before = [IO.File]::ReadAllText((Join-Path $Project 'custom.sty'))
$after = [IO.File]::ReadAllText($styFile)
$expectedAdded = @("\Marker $stockMarker", '\Bold -', "\ColorName $colorName", "\Marker $newMarker", "\Name $newName")
$added = if ($after.StartsWith($before)) { @($after.Substring($before.Length) -split "`r?`n" | Where-Object { $_.Trim() }) } else { $null }
"added lines: $($added -join ' | ')"
if ($null -eq $added -or @($expectedAdded | Where-Object { $added -notcontains $_ }).Count -or @($added | Where-Object { $_ -notmatch '^\\(Marker|Bold|ColorName|Name|StyleType|Endmarker|TextType|TextProperties) ' -and $_ -ne '\Bold -' }).Count) {
    throw "the saved file is not the original plus the two new entries"
}
'saved: the file is the original plus the two new entries'

# After saving: no star in the status bar, and the reminder to restart Paratext.
$banner = OfType $script:win $CT::Text | Where-Object { $_.Current.Name -like (T 'Paratext reads custom.sty*' 'Paratext solo lee custom.sty*' 'Paratext ne lit custom.sty*') } | Select-Object -First 1
Step 's09_restart' @(@(ById 'StatusText'), @($banner)) -1

$script:win.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
$script:proc.WaitForExit(10000) | Out-Null
if (-not $script:proc.HasExited) { $script:proc.Kill(); $script:proc.WaitForExit(5000) | Out-Null }
RemoveDemoProject
$script:steps | Export-Clixml -LiteralPath $stepsFile
}
if ($CaptureOnly) { "captured in ${Work}; build the video with -Reuse"; return }

# The Paratext Tools menu (tools_menu_<lang>.png), before the window opens. The boxes are in each
# picture's pixels: the Tools heading and the plugin's menu item. tools_menu_en.png (1291 x 919)
# is the user's screenshot of 2026-11-05, tools_menu_es.png (1472 x 922) and tools_menu_fr.png
# (1629 x 922) of 2026-11-06; a new screenshot needs new numbers.
$menuBoxes = @{
    'en' = @(@(1037, 101, 54, 24), @(1037, 329, 196, 24))
    'es' = @(@(1134, 101, 98, 24), @(1134, 329, 282, 24))
    'fr' = @(@(1289, 101, 46, 24), @(1289, 329, 290, 24))
}
$menuShot = Join-Path $here "tools_menu_$Lang.png"
if ((Test-Path -LiteralPath $menuShot) -and $menuBoxes.ContainsKey($Lang)) {
    $mb = @(); foreach ($b in $menuBoxes[$Lang]) { $mb += , $b }
    $menuStep = [pscustomobject]@{ Id = 's00_menu'; Shot = $menuShot; Boxes = $mb; Pointer = 1; Dialog = $null; Rows = -1 }
    $script:steps = @($menuStep) + $script:steps
}
else { "no $menuShot (or no boxes for it): the menu step is left out" }

# Getting and installing the tool: the shared install pictures (the standard, section 9), after
# the last step. Each is demo\<File>_<lang>.png, or the English one when there is none for the
# language. Boxes are in that picture's pixels (x, y, width, height); Pointer = the box pointed at.
# Getting and installing the tool: the shared install section (Install_Phils_tools.exe, see
# tools\demo_video\install\InstallSection.ps1), then this plugin's Tools menu again.
. (Join-Path (Split-Path (Split-Path $here)) 'tools\demo_video\install\InstallSection.ps1')
$script:steps += Get-InstallSteps -Plugin 'CustomSty' -Lang $Lang -Name (Text 'title').title
$installSteps = @(
    @{ Id = 'i5_ready';    File = 'tools_menu';         Boxes = 'menu'; Pointer = 1 }
)
foreach ($is in $installSteps) {
    $shot = Join-Path $here ("{0}_{1}.png" -f $is.File, $Lang)
    if (-not (Test-Path -LiteralPath $shot)) { $shot = Join-Path $here ("{0}_en.png" -f $is.File) }
    if (-not (Test-Path -LiteralPath $shot)) { "no $shot : step $($is.Id) is left out"; continue }
    $shotLang = if ($shot -like "*_$Lang.png") { $Lang } else { 'en' }
    $boxList = $is.Boxes
    if ($boxList -is [hashtable]) { $boxList = $boxList[$shotLang] }
    elseif ($boxList -eq 'menu') { $boxList = $menuBoxes[$shotLang] }
    $bx = @(); foreach ($b in $boxList) { $bx += , $b }
    $script:steps += [pscustomobject]@{ Id = $is.Id; Shot = $shot; Boxes = $bx; Pointer = $is.Pointer; Dialog = $null; Rows = -1 }
}

# ------------------------------------------------------------ frames
$W = 1920; $H = 1080; $top = 96; $bottomBand = 190
# The "Sky" design (the user's choice): deep blue cover, light-to-mid blue steps, gold accents.
function Hex($h) { [Drawing.ColorTranslator]::FromHtml($h) }
$navy = Hex '#1F3A5F'          # headline bar
$orange = [Drawing.Color]::FromArgb(255, 140, 0)   # boxes round the controls
$gold = Hex '#F2B544'          # accents
$band = Hex '#18222E'          # subtitle band
$bgTop = Hex '#EEF7FF'; $bgBottom = Hex '#94BEE4'          # steps: the gradient
$coverTop = Hex '#2C5A9A'; $coverBottom = Hex '#0B1830'    # cover and closing slide
function Gradient($g, $x, $y, $w, $h, $c1, $c2) {
    $br = New-Object Drawing.Drawing2D.LinearGradientBrush (New-Object Drawing.RectangleF $x, ($y - 1), $w, ($h + 2)), $c1, $c2, 90.0
    $g.FillRectangle($br, [float]$x, [float]$y, [float]$w, [float]$h)
    $br.Dispose()
}
function RoundRect($x, $y, $w, $h, $r) {
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc([float]$x, [float]$y, $d, $d, 180, 90); $p.AddArc([float]($x + $w - $d), [float]$y, $d, $d, 270, 90)
    $p.AddArc([float]($x + $w - $d), [float]($y + $h - $d), $d, $d, 0, 90); $p.AddArc([float]$x, [float]($y + $h - $d), $d, $d, 90, 90)
    $p.CloseFigure(); $p
}
function NewFrame { $f = New-Object Drawing.Bitmap $W, $H; $g = [Drawing.Graphics]::FromImage($f); $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'; $g.InterpolationMode = 'HighQualityBicubic'; @{ F = $f; G = $g } }
function Centered($g, $text, $font, $brush, $rect) {
    $sf = New-Object Drawing.StringFormat
    $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
    $g.DrawString($text, $font, $brush, $rect, $sf)
}
# A font of the given size, or smaller until the text fits on one line in $maxWidth pixels
# (long names, e.g. "Edicion de notas en lote", were cut off on the cover).
function FitFont($g, $text, $family, $size, $maxWidth) {
    $f = New-Object Drawing.Font $family, $size
    while ($size -gt 36 -and $g.MeasureString($text, $f).Width -gt $maxWidth) {
        $f.Dispose(); $size -= 2; $f = New-Object Drawing.Font $family, $size
    }
    $f
}
function Card($id, $file) {
    $t = Text $id
    $fr = NewFrame; $g = $fr.G
    Gradient $g 0 0 $W $H $coverTop $coverBottom
    $light = New-Object Drawing.SolidBrush $gold
    $grey = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(190, 205, 225))
    if ($id -eq 'title') {
        # The cover: a framed panel (gold border) with a tag on its top edge; the plugin's name
        # large and white, a gold rule, then the series smaller (the user's order).
        $px = 360; $py = 290; $pw = $W - 720; $ph = 470
        $panel = RoundRect $px $py $pw $ph 28
        $g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(40, 255, 255, 255))), $panel)
        $g.DrawPath((New-Object Drawing.Pen $gold, 4), $panel)
        $tagFont = New-Object Drawing.Font 'Segoe UI Semibold', 20
        $tagText = T 'PARATEXT PLUGIN' 'COMPLEMENTO DE PARATEXT' 'MODULE PARATEXT'
        $tw = $g.MeasureString($tagText, $tagFont).Width + 44; $th = 48; $tx = ($W - $tw) / 2; $ty = $py - $th / 2
        $g.FillPath((New-Object Drawing.SolidBrush $gold), (RoundRect $tx $ty $tw $th 24))
        Centered $g $tagText $tagFont (New-Object Drawing.SolidBrush $coverBottom) (New-Object Drawing.RectangleF $tx, $ty, $tw, $th)
        Centered $g $t.title (FitFont $g $t.title 'Segoe UI Semibold' 80 ($pw - 60)) ([Drawing.Brushes]::White) (New-Object Drawing.RectangleF $px, ($py + 60), $pw, 170)
        $g.FillRectangle($light, [float]($W / 2 - 160), [float]($py + 250), 320, 5)
        Centered $g $Series (New-Object Drawing.Font 'Segoe UI', 50) $light (New-Object Drawing.RectangleF $px, ($py + 280), $pw, 120)
    }
    elseif ($id -like 'sec_*') {
        # A section slide (the user's "subtitle" slide): the cover's panel, its title large and
        # white, a gold rule, then the plugin's name smaller.
        $px = 360; $py = 290; $pw = $W - 720; $ph = 470
        $panel = RoundRect $px $py $pw $ph 28
        $g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(40, 255, 255, 255))), $panel)
        $g.DrawPath((New-Object Drawing.Pen $gold, 4), $panel)
        Centered $g $t.title (FitFont $g $t.title 'Segoe UI Semibold' 80 ($pw - 60)) ([Drawing.Brushes]::White) (New-Object Drawing.RectangleF $px, ($py + 60), $pw, 170)
        $g.FillRectangle($light, [float]($W / 2 - 160), [float]($py + 250), 320, 5)
        # The plugin's name, smaller when it would not fit on one line (the French name was cut).
        $name = (Text 'title').title
        Centered $g $name (FitFont $g $name 'Segoe UI' 50 ($pw - 60)) $light (New-Object Drawing.RectangleF $px, ($py + 280), $pw, 120)
    }
    else {
        $f1 = New-Object Drawing.Font 'Segoe UI Semibold', 72
        $f2 = New-Object Drawing.Font 'Segoe UI', 34
        # Smaller when a long name would not fit on one line (the French name of this tool).
        $f1 = FitFont $g $t.title 'Segoe UI Semibold' 72 ($W - 200)
        Centered $g $t.title $f1 ([Drawing.Brushes]::White) (New-Object Drawing.RectangleF 80, 300, ($W - 160), 160)
        $g.FillRectangle($light, [float]($W / 2 - 160), 462, 320, 5)
        # The key points on one line, or each on its own line when they don't fit (a wrap fell in
        # the middle of a point in Spanish and French).
        $sub = $t.sub
        $subH = 120
        if ($g.MeasureString($sub, $f2).Width -gt ($W - 160)) { $sub = ($sub -split ('\s+' + [char]0xB7 + '\s+')) -join "`n"; $subH = 200 }
        Centered $g $sub $f2 $light (New-Object Drawing.RectangleF 80, 480, ($W - 160), $subH)
        # Credits, in small print.
        $credits = @(T 'Created with Claude AI' 'Creado con Claude AI' "Cr$([char]0xE9)$([char]0xE9) avec Claude AI")
        if (-not $NoVoice -and $azure) { $credits += T 'Narration: AI-generated voice (Microsoft Azure AI Speech)' "Narraci$([char]0xF3)n: voz generada por IA (Microsoft Azure AI Speech)" "Narration : voix g$([char]0xE9)n$([char]0xE9)r$([char]0xE9)e par IA (Microsoft Azure AI Speech)" }
        if ($musicOn) { $credits += T 'Music: Coconut Kind of Love by Aetheric (freetouse.com)' "M$([char]0xFA)sica: Coconut Kind of Love de Aetheric (freetouse.com)" 'Musique : Coconut Kind of Love par Aetheric (freetouse.com)' }
        $f3 = New-Object Drawing.Font 'Segoe UI', 22
        $y = $H - 70 - 42 * $credits.Count
        foreach ($c in $credits) { Centered $g $c $f3 $grey (New-Object Drawing.RectangleF 80, $y, ($W - 160), 42); $y += 42 }
    }
    $g.Dispose(); $fr.F.Save($file, [Drawing.Imaging.ImageFormat]::Png); $fr.F.Dispose()
}
function Pointer($g, $x, $y) {
    # A mouse arrow with its tip at (x, y).
    $s = 1.6
    $pts = @(@(0, 0), @(0, 25), @(6, 19), @(11, 30), @(15, 28), @(10, 18), @(18, 18)) | ForEach-Object { New-Object Drawing.PointF ($x + $_[0] * $s), ($y + $_[1] * $s) }
    $g.FillPolygon([Drawing.Brushes]::White, [Drawing.PointF[]]$pts)
    $pen = New-Object Drawing.Pen ([Drawing.Color]::Black), 2
    $g.DrawPolygon($pen, [Drawing.PointF[]]$pts)
}
# The window area of a frame: the band between the headline and the subtitles, less margins.
$areaX = 40; $areaY = $top + 22; $areaW = $W - 80; $areaH = $H - $top - $bottomBand - 44
# The view of the whole window: the shot widened (or heightened) to the area's shape.
function FullView($iw, $ih) {
    $ar = $areaW / $areaH
    if ($iw / $ih -lt $ar) { $w0 = $ih * $ar; , @((($iw - $w0) / 2), 0, $w0, $ih) }
    else { $h0 = $iw / $ar; , @(0, (($ih - $h0) / 2), $iw, $h0) }
}
# The close-up of a step: the boxes (and the dialog) with a margin, in the area's shape, at most
# twice the size of the full view; $null when that would hardly be closer than the full view.
function ZoomView($st, $iw, $ih) {
    $rects = @($st.Boxes)
    if ($st.Dialog) { $rects += , $st.Dialog }
    if ($rects.Count -eq 0) { return $null }
    $x1 = 1e9; $y1 = 1e9; $x2 = -1e9; $y2 = -1e9
    foreach ($r in $rects) {
        $x1 = [Math]::Min($x1, $r[0]); $y1 = [Math]::Min($y1, $r[1])
        $x2 = [Math]::Max($x2, $r[0] + $r[2]); $y2 = [Math]::Max($y2, $r[1] + $r[3])
    }
    $pad = 0.05 * $iw
    $x1 -= $pad; $y1 -= $pad; $x2 += $pad; $y2 += $pad
    $ar = $areaW / $areaH
    $w = $x2 - $x1; $h = $y2 - $y1
    if ($w / $h -lt $ar) { $w = $h * $ar } else { $h = $w / $ar }
    $full = FullView $iw $ih
    $maxZoom = 2.0
    if ($full[2] / $w -gt $maxZoom) { $w = $full[2] / $maxZoom; $h = $w / $ar }
    if ($full[2] / $w -lt 1.15) { return $null }
    $cx = ($x1 + $x2) / 2; $cy = ($y1 + $y2) / 2
    $x = $cx - $w / 2; $y = $cy - $h / 2
    if ($w -le $iw) { $x = [Math]::Max(0, [Math]::Min($x, $iw - $w)) } else { $x = ($iw - $w) / 2 }
    if ($h -le $ih) { $y = [Math]::Max(0, [Math]::Min($y, $ih - $h)) } else { $y = ($ih - $h) / 2 }
    , @($x, $y, $w, $h)
}
# Between two views, at s (0..1): the centre moves in a straight line, the size changes
# geometrically, so the zoom feels even.
function MixView($a, $b, $s) {
    $w = $a[2] * [Math]::Pow($b[2] / $a[2], $s); $h = $a[3] * [Math]::Pow($b[3] / $a[3], $s)
    $cx = ($a[0] + $a[2] / 2) * (1 - $s) + ($b[0] + $b[2] / 2) * $s
    $cy = ($a[1] + $a[3] / 2) * (1 - $s) + ($b[1] + $b[3] / 2) * $s
    , @(($cx - $w / 2), ($cy - $h / 2), $w, $h)
}
# $part: one part of a step with cues (its Show = the boxes shown); $null = all boxes.
function StepFrame($st, $num, $total, $file, $view = $null, $part = $null) {
    $t = Text $st.Id
    $fr = NewFrame; $g = $fr.G
    Gradient $g 0 $top $W ($H - $top - $bottomBand) $bgTop $bgBottom
    # headline, with a gold line under it
    $g.FillRectangle((New-Object Drawing.SolidBrush $navy), 0, 0, $W, $top)
    $g.FillRectangle((New-Object Drawing.SolidBrush $gold), 0, ($top - 5), $W, 5)
    $fh = New-Object Drawing.Font 'Segoe UI Semibold', 30
    $g.DrawString($t.title, $fh, [Drawing.Brushes]::White, 40, 22)
    $fn = New-Object Drawing.Font 'Segoe UI', 20
    $sf = New-Object Drawing.StringFormat; $sf.Alignment = 'Far'
    $g.DrawString("$num / $total", $fn, (New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(190, 205, 225))), (New-Object Drawing.RectangleF 0, 34, ($W - 40), 40), $sf)
    # the window: the view (a rectangle of the shot) fills the area; float coordinates, so a
    # zoom moves smoothly. Whatever falls outside the band between headline and subtitles is cut.
    $img = [Drawing.Image]::FromFile($st.Shot)
    if (-not $view) { $view = FullView $img.Width $img.Height }
    $sc = [Math]::Min($areaW / $view[2], $areaH / $view[3])
    $ox = $areaX + ($areaW - $view[2] * $sc) / 2 - $view[0] * $sc
    $oy = $areaY + ($areaH - $view[3] * $sc) / 2 - $view[1] * $sc
    $iw = $img.Width * $sc; $ih = $img.Height * $sc
    $g.SetClip((New-Object Drawing.RectangleF 0, $top, $W, ($H - $top - $bottomBand)))
    # a soft shadow (spread, faint layers) and a white frame, so the grey window floats on the page
    for ($k = 12; $k -ge 2; $k -= 2) {
        $g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(12, 10, 30, 60))), (RoundRect ($ox - $k + 6) ($oy - $k + 10) ($iw + 2 * $k) ($ih + 2 * $k) (10 + $k)))
    }
    $g.FillPath([Drawing.Brushes]::White, (RoundRect ($ox - 6) ($oy - 6) ($iw + 12) ($ih + 12) 10))
    $g.DrawImage($img, [float]$ox, [float]$oy, [float]$iw, [float]$ih)
    $pen = New-Object Drawing.Pen $orange, 5
    if ($st.Dialog) {
        $d = $st.Dialog
        $g.DrawRectangle((New-Object Drawing.Pen ([Drawing.Color]::FromArgb(31, 58, 95)), 4), [float]($ox + $d[0] * $sc - 4), [float]($oy + $d[1] * $sc - 4), [float]($d[2] * $sc + 8), [float]($d[3] * $sc + 8))
    }
    $i = 0; $pointAtShown = -not $part
    foreach ($b in $st.Boxes) {
        $shown = (-not $part) -or ($part.Show -contains $i)
        if ($shown) {
            $bx = $ox + $b[0] * $sc - 6; $by = $oy + $b[1] * $sc - 6; $bwid = $b[2] * $sc + 12; $bhei = $b[3] * $sc + 12
            $g.DrawRectangle($pen, [float]$bx, [float]$by, [float]$bwid, [float]$bhei)
            # With cues, the pointer (if the step has one) is on the first box named.
            $pointHere = if ($part) { $st.Pointer -ge 0 -and $part.Show[0] -eq $i } else { $i -eq $st.Pointer }
            if ($pointHere) { Pointer $g ($bx + $bwid * 0.6) ($by + $bhei * 0.6) }
            if ($st.PointAt) {
                $pa = $st.PointAt; $cx = $pa[0] + $pa[2] / 2; $cy = $pa[1] + $pa[3] / 2
                if ($cx -ge $b[0] -and $cx -le $b[0] + $b[2] -and $cy -ge $b[1] -and $cy -le $b[1] + $b[3]) { $pointAtShown = $true }
            }
        }
        $i++
    }
    if ($st.PointAt -and $pointAtShown) {
        $pa = $st.PointAt
        # at the right end of the item, so its text stays readable
        Pointer $g ($ox + ($pa[0] + $pa[2] * 0.88) * $sc) ($oy + ($pa[1] + $pa[3] * 0.5) * $sc)
    }
    $g.ResetClip()
    $iwShot = $img.Width; $ihShot = $img.Height
    $img.Dispose()
    # subtitles
    $g.FillRectangle((New-Object Drawing.SolidBrush $band), 0, ($H - $bottomBand), $W, $bottomBand)
    $fs = New-Object Drawing.Font 'Segoe UI', 27
    Centered $g (Clean $t.text) $fs ([Drawing.Brushes]::White) (New-Object Drawing.RectangleF 70, ($H - $bottomBand + 8), ($W - 140), ($bottomBand - 16))
    $g.Dispose(); $fr.F.Save($file, [Drawing.Imaging.ImageFormat]::Png); $fr.F.Dispose()
}

$order = @('title')
foreach ($st in $script:steps) {
    if ($st.Id -eq 'd1_folder') { $order += 'sec_install' }
    $order += $st.Id
}
$order += 'end'
# Slides (cover, section slides, closing slide) have no step number.
function IsCard($id) { $id -eq 'title' -or $id -eq 'end' -or $id -like 'sec_*' }
$stepIds = @($order | Where-Object { -not (IsCard $_) })
# Zoom timing: it starts when the dissolve into the step is over, and takes $zoomTime seconds.
$zoomStart = 1.3; $zoomTime = 1.6; $zoomFrames = [int]($zoomTime * 30)
$zooms = @{}; $partFrames = @{}
$n = 0
foreach ($id in $order) {
    $file = Join-Path $frames ('{0:D2}_{1}.png' -f $n, $id)
    if (IsCard $id) { Card $id $file }
    else {
        $st = $script:steps | Where-Object { $_.Id -eq $id }
        $num = [array]::IndexOf($stepIds, $id) + 1
        StepFrame $st $num $stepIds.Count $file
        $cues = Cues (Text $id).text
        if ($cues.Count) {
            $pf = @()
            for ($k = 0; $k -lt $cues.Count; $k++) {
                $cfile = Join-Path $frames ('{0:D2}_{1}_c{2}.png' -f $n, $id, $k)
                StepFrame $st $num $stepIds.Count $cfile $null $cues[$k]
                $pf += $cfile
            }
            $partFrames[$n] = $pf
            "cues $id : $(($cues | ForEach-Object { '{' + ($_.Show -join ',') + '}' }) -join ' ')"
        }
        elseif ($Zoom) {
            $img = [Drawing.Image]::FromFile($st.Shot); $iw0 = $img.Width; $ih0 = $img.Height; $img.Dispose()
            $v1 = ZoomView $st $iw0 $ih0
            if ($v1) {
                $v0 = FullView $iw0 $ih0
                $zf = @()
                for ($k = 1; $k -le $zoomFrames; $k++) {
                    $pp = $k / $zoomFrames
                    $ss = $pp * $pp * (3 - 2 * $pp)     # ease in and out
                    $zfile = Join-Path $frames ('{0:D2}_z{1:D3}.png' -f $n, $k)
                    StepFrame $st $num $stepIds.Count $zfile (MixView $v0 $v1 $ss)
                    $zf += $zfile
                }
                $zooms[$n] = $zf
                "zoom $id"
            }
        }
    }
    $n++
}

# ------------------------------------------------------------ speech and video
# Spoken text is kept between runs ($Work is emptied each time), so a rebuild asks Azure only for
# what changed (the free tier allows few requests a minute).
$ttsCache = Join-Path $env:TEMP 'bulknotes_demo_tts'
New-Item -ItemType Directory -Force $ttsCache | Out-Null
function Say($text, $wav) {
    if (-not $azure) { $tts.SetOutputToWaveFile($wav); $tts.Speak($text); $tts.SetOutputToNull(); return }
    $keyText = "$Voice|$AzureRate|$text|" + ($script:SayAs | ConvertTo-Json -Compress -Depth 4)
    $sha = [Security.Cryptography.SHA1]::Create()
    $hash = ($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($keyText)) | ForEach-Object { $_.ToString('x2') }) -join ''
    $cached = Join-Path $ttsCache "$hash.wav"
    if (-not (Test-Path -LiteralPath $cached)) { Speak-Azure -Text $text -Voice $Voice -OutFile $cached -Rate $AzureRate }
    Copy-Item -LiteralPath $cached $wav -Force
}
# Seconds from the start of a WAV (16-bit PCM, data at byte 44) to the end of the speech in it.
function SpeechEnd($wav) {
    $bytes = [IO.File]::ReadAllBytes($wav)
    $byteRate = [BitConverter]::ToInt32($bytes, 28)
    $i = $bytes.Length - 2
    while ($i -ge 44 -and [Math]::Abs([int][BitConverter]::ToInt16($bytes, $i)) -lt 700) { $i -= 2 }
    [Math]::Max(0.0, ($i - 44) / $byteRate)
}
if (-not $NoVoice) {
    if ($azure) { . (Join-Path $here 'azure_tts.ps1') }
    else {
        $tts = New-Object System.Speech.Synthesis.SpeechSynthesizer
        $tts.SelectVoice($Voice)
        $tts.Rate = $Rate
    }
    "voice: $Voice"
}
else { 'voice: none' }
$ffmpeg = $FFmpeg
if (-not $ffmpeg) {
    $ffmpeg = Get-ChildItem 'C:\ffmpeg-*' -Directory -ErrorAction SilentlyContinue |
        ForEach-Object { Get-ChildItem $_.FullName -Recurse -Filter ffmpeg.exe -ErrorAction SilentlyContinue } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $ffmpeg) { $ffmpeg = (Get-Command ffmpeg).Source }
"ffmpeg: $ffmpeg"
if (-not ((& $ffmpeg -hide_banner -filters 2>&1 | Out-String) -match '\bxfade\b')) {
    throw "This ffmpeg has no xfade filter (it needs ffmpeg 4.3 or later): $ffmpeg"
}
$inv = [Globalization.CultureInfo]::InvariantCulture
function Num($x) { ([double]$x).ToString('0.###', $inv) }

# The joins: a dip to black after the cover and before the closing slide, dissolves elsewhere.
$count = $order.Count
$joinType = @{}; $joinLen = @{}
for ($k = 1; $k -lt $count; $k++) {
    if ((IsCard $order[$k - 1]) -or (IsCard $order[$k])) { $joinType[$k] = 'fadeblack'; $joinLen[$k] = $Dip }
    else {
        $ra = ($script:steps | Where-Object { $_.Id -eq $order[$k - 1] }).Rows
        $rb = ($script:steps | Where-Object { $_.Id -eq $order[$k] }).Rows
        if ($ra -ge 0 -and $ra -eq $rb) { $joinType[$k] = 'wipeleft'; $joinLen[$k] = $Wipe }
        else { $joinType[$k] = 'fade'; $joinLen[$k] = $Dissolve }
    }
    "join $($order[$k - 1]) -> $($order[$k]): $($joinType[$k]) $($joinLen[$k]) s"
}
$fadeInFirst = 0.8; $endHold = 1.5
# Each step: its join in, a breath, the voice (or the reading time), a pause, its join out.
$args2 = @('-hide_banner', '-loglevel', 'error', '-y')
$durs = @(); $leads = @(); $voiceIdx = @{}; $videoIdx = @{}
$idx = 0
for ($n = 0; $n -lt $count; $n++) {
    $id = $order[$n]
    $frame = Join-Path $frames ('{0:D2}_{1}.png' -f $n, $id)
    $in = if ($n -eq 0) { $fadeInFirst } else { $joinLen[$n] }
    $out = if ($n -eq $count - 1) { $endHold } else { $joinLen[$n + 1] }
    $lead = $in + 0.3; $tail = 0.8
    if ($n -eq 0) { $lead += $CoverIntro }
    $text = Clean (Text $id).text
    if ($NoVoice) {
        # About 2.6 words a second: time to read the subtitle.
        $words = @($text -split '\s+' | Where-Object { $_ }).Count
        $secs = [Math]::Max(2.5, $words / 2.6)
        # The cover and closing slides show no subtitle: a short, fixed time.
        if ($id -eq 'title') { $secs = 1.5 } elseif ($id -eq 'end') { $secs = 4.5 } elseif ($id -like 'sec_*') { $secs = 2.5 }
        $wav = $null
    }
    else {
        $wav = Join-Path $Work ('{0:D2}.wav' -f $n)
        Say $text $wav
        $fs = [IO.File]::OpenRead($wav); $br = New-Object IO.BinaryReader $fs; $fs.Position = 28; $rate = $br.ReadInt32(); $br.Close()
        $secs = ((Get-Item $wav).Length - 44) / $rate
    }
    $dur = [Math]::Round([Math]::Max($lead + $secs + $tail + $out, 4.5), 2)
    $durs += $dur; $leads += $lead
    if ($partFrames.ContainsKey($n)) {
        # When each part starts: where its cue is spoken (the speech of the text before the cue,
        # spoken on its own), or without a voice, its share of the words.
        $cues = Cues (Text $id).text
        $starts = @()
        for ($k = 0; $k -lt $cues.Count; $k++) {
            $pos = $cues[$k].Pos
            if ($pos -eq 0) { $starts += 0.0; continue }
            $before = $text.Substring(0, $pos).Trim()
            if ($wav) {
                $pw = Join-Path $Work ('{0:D2}_cue{1}.wav' -f $n, $k)
                Say $before $pw
                $t = SpeechEnd $pw
            }
            else {
                $all = @($text -split '\s+' | Where-Object { $_ }).Count
                $t = $secs * @($before -split '\s+' | Where-Object { $_ }).Count / [Math]::Max(1, $all)
            }
            $starts += [Math]::Min($dur - 0.5, $lead + $t)
        }
        "  cues at $(($starts | ForEach-Object { Num $_ }) -join ', ') s"
        $q = { param($x) "file '" + ($x -replace '\\', '/') + "'" }
        $lines = @('ffconcat version 1.0')
        $pf = $partFrames[$n]
        for ($k = 0; $k -lt $pf.Count; $k++) {
            $end = if ($k -lt $pf.Count - 1) { $starts[$k + 1] } else { $dur + 1 }
            $lines += (& $q $pf[$k]); $lines += "duration $(Num ([Math]::Max(0.034, $end - $starts[$k])))"
        }
        $lines += (& $q $pf[-1])     # the concat reader ignores the last duration
        $listFile = Join-Path $Work ('{0:D2}_cues.txt' -f $n)
        [IO.File]::WriteAllLines($listFile, $lines)
        # Made into a clip of its own first: many image-list inputs in the one big join ran ffmpeg
        # out of memory.
        $clip = Join-Path $Work ('{0:D2}_cues.mp4' -f $n)
        & $ffmpeg -hide_banner -loglevel error -y -f concat -safe 0 -i $listFile -vf 'fps=30,format=yuv420p' -t (Num $dur) -c:v libx264 -preset veryfast -crf 12 -tune stillimage -threads 4 $clip
        if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed on the cues of $id" }
        # One decoding thread each: with frame threads, every clip's decoder holds many 1080p
        # frames, and the big join ran out of memory.
        $args2 += @('-threads', '1', '-i', $clip)
    }
    elseif ($zooms.ContainsKey($n)) {
        $zf = $zooms[$n]
        $q = { param($x) "file '" + ($x -replace '\\', '/') + "'" }
        $zs = $in + 0.3
        $lines = @('ffconcat version 1.0', (& $q $frame), "duration $(Num $zs)")
        foreach ($z in $zf) { $lines += (& $q $z); $lines += 'duration 0.0333333' }
        $lines += (& $q $zf[-1]); $lines += "duration $(Num ($dur - $zs - $zoomTime + 1))"
        $lines += (& $q $zf[-1])     # the concat reader ignores the last duration
        $listFile = Join-Path $Work ('{0:D2}_zoom.txt' -f $n)
        [IO.File]::WriteAllLines($listFile, $lines)
        $args2 += @('-f', 'concat', '-safe', '0', '-i', $listFile)
    }
    else { $args2 += @('-loop', '1', '-framerate', '30', '-t', (Num $dur), '-i', $frame) }
    $videoIdx[$n] = $idx; $idx++
    if ($wav) { $args2 += @('-i', $wav); $voiceIdx[$n] = $idx; $idx++ }
    "step $id $(Num $dur)s"
}
if (-not $NoVoice -and -not $azure) { $tts.Dispose() }
if ($musicOn) { $args2 += @('-stream_loop', '-1', '-i', $Music); $musicIdx = $idx; $idx++ }

# Video: xfade joins. Each join starts $joinLen before the end of what is joined so far.
$f = @()
for ($n = 0; $n -lt $count; $n++) {
    $f += "[$($videoIdx[$n]):v]fps=30,format=yuv420p,setsar=1,trim=0:$(Num $durs[$n]),settb=AVTB,setpts=PTS-STARTPTS[v$n]"
}
$length = $durs[0]; $vPrev = 'v0'
for ($k = 1; $k -lt $count; $k++) {
    $f += "[$vPrev][v$k]xfade=transition=$($joinType[$k]):duration=$(Num $joinLen[$k]):offset=$(Num ($length - $joinLen[$k]))[xv$k]"
    $vPrev = "xv$k"
    $length += $durs[$k] - $joinLen[$k]
}
$total = $length
$f += "[$vPrev]fade=t=in:st=0:d=$(Num $fadeInFirst),fade=t=out:st=$(Num ($total - 1.2)):d=1.2[vout]"

# Audio: the voices, each delayed to its place, joined with the same overlaps; the music under it.
$mix = $null
if (-not $NoVoice) {
    for ($n = 0; $n -lt $count; $n++) {
        $ms = [int]($leads[$n] * 1000)
        $f += "[$($voiceIdx[$n]):a]aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo,adelay=$ms|$ms,apad,atrim=0:$(Num $durs[$n]),asetpts=PTS-STARTPTS[a$n]"
    }
    $aPrev = 'a0'
    for ($k = 1; $k -lt $count; $k++) {
        $f += "[$aPrev][a$k]acrossfade=d=$(Num $joinLen[$k]):c1=tri:c2=tri[xa$k]"
        $aPrev = "xa$k"
    }
    $mix = $aPrev
}
if ($musicOn) {
    # The music alone at $MusicVolume, then down to $MusicUnderVoice over the second before the
    # narration starts (where it would start, without a voice), and there for the rest.
    $t1 = $leads[0]; $t0 = [Math]::Max(0.0, $t1 - 1.0)
    $hi = Num $MusicVolume; $lo = Num $MusicUnderVoice
    $level = "if(lt(t,$(Num $t0)),$hi,if(lt(t,$(Num $t1)),$hi-($hi-$lo)*(t-$(Num $t0))/$(Num ($t1 - $t0)),$lo))"
    $f += "[$($musicIdx):a]aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo,atrim=0:$(Num $total),asetpts=PTS-STARTPTS,volume='$level':eval=frame,afade=t=in:st=0:d=2,afade=t=out:st=$(Num ($total - 3)):d=3[music]"
    if ($mix) {
        $f += "[$mix][music]amix=inputs=2:duration=first:normalize=0[amixed]"
        $mix = 'amixed'
    }
    else { $mix = 'music' }
}
if (-not $mix) { $f += "anullsrc=r=48000:cl=stereo,atrim=0:$(Num $total)[silence]"; $mix = 'silence' }
$f += "[$mix]afade=t=out:st=$(Num ($total - 1.2)):d=1.2[aout]"

$script = Join-Path $Work 'filter.txt'
[IO.File]::WriteAllText($script, ($f -join ";`n"))
$suffix = $(if ($Zoom) { '_zoom' } else { '' }) + $(if ($NoVoice) { '_novoice' } else { '' })
$outFile = Join-Path $here ("CustomStyEditor_Demo_{0}{1}.mp4" -f $Lang.ToUpper(), $suffix)
# Newer ffmpeg reads an option's value from a file with -/option; older ones had -filter_complex_script.
$fromFile = if ((& $ffmpeg -hide_banner -h long 2>&1 | Out-String) -match 'filter_complex_script') { '-filter_complex_script' } else { '-/filter_complex' }
$args2 += @($fromFile, $script, '-map', '[vout]', '-map', '[aout]', '-t', (Num $total),
    # Four encoder threads: fewer buffers, so the join fits when memory is short.
    '-c:v', 'libx264', '-preset', 'medium', '-tune', 'stillimage', '-crf', '18', '-r', '30', '-threads', '4',
    '-c:a', 'aac', '-b:a', '160k', '-movflags', '+faststart', $outFile)
& $ffmpeg @args2
if ($LASTEXITCODE -ne 0) { throw 'ffmpeg failed' }
"written $outFile ($(Num $total)s)"

# The YouTube description (the standard, section 12), with each step's question ('q' in
# narration.json) at the time its step is fully on screen in this video: the end of the join into
# it, rounded up. Only for the voiced video, whose times differ from the test's. English is
# youtubetext.txt (the user's name for it), other languages youtubetext_<lang>.txt.
if (-not $NoVoice) {
    $yt = $narr.youtube.$Lang
    if (-not $yt) { "narration.json has no youtube text for '$Lang': no youtubetext written" }
    else {
        $lines = @($yt.about, '', $yt.download, $yt.link, '')
        $at = $durs[0]
        for ($k = 1; $k -lt $count; $k++) {
            $q = (Text $order[$k]).q
            if ($q) { $sec = [int][Math]::Ceiling($at); $lines += ('{0}:{1:D2} {2}' -f [int][Math]::Floor($sec / 60), ($sec % 60), $q) }
            $at += $durs[$k] - $joinLen[$k]
        }
        $lines += ''
        $lines += @($yt.credits)
        $ytFile = Join-Path $here $(if ($Lang -eq 'en') { 'youtubetext.txt' } else { "youtubetext_$Lang.txt" })
        [IO.File]::WriteAllText($ytFile, (($lines -join "`r`n") + "`r`n"), (New-Object Text.UTF8Encoding $true))
        "written $ytFile"
    }
}
