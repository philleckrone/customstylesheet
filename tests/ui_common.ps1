# Shared UI Automation helpers for the Custom Stylesheet Editor tests (dot-source this file).
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

$script:AE = [System.Windows.Automation.AutomationElement]
$script:TS = [System.Windows.Automation.TreeScope]

function WaitForWindow($proc) {
    $win = $null
    for ($i = 0; $i -lt 60 -and -not $win; $i++) {
        Start-Sleep -Milliseconds 500; $proc.Refresh()
        if ($proc.MainWindowHandle -ne 0) { $win = $script:AE::FromHandle($proc.MainWindowHandle) }
    }
    if (-not $win) { throw 'Editor window did not appear' }
    Start-Sleep -Seconds 2
    return $win
}

function FindById($root, [string]$id) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($script:AE::AutomationIdProperty, $id)
    return $root.FindFirst($script:TS::Descendants, $cond)
}

function FindByType($root, $type) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($script:AE::ControlTypeProperty, $type)
    return $root.FindAll($script:TS::Descendants, $cond)
}

function FindByName($root, [string]$name, $type = $null) {
    $c1 = New-Object System.Windows.Automation.PropertyCondition($script:AE::NameProperty, $name)
    if ($type) {
        $c2 = New-Object System.Windows.Automation.PropertyCondition($script:AE::ControlTypeProperty, $type)
        $c1 = New-Object System.Windows.Automation.AndCondition($c1, $c2)
    }
    return $root.FindFirst($script:TS::Descendants, $c1)
}

function GridRows($win) {
    $grid = FindById $win 'MarkerGrid'
    $cond = New-Object System.Windows.Automation.PropertyCondition($script:AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::DataItem)
    return $grid.FindAll($script:TS::Children, $cond)
}

function SelectGridRow($win, [int]$index) {
    $rows = GridRows $win
    if ($rows.Count -le $index) { throw "Marker list has only $($rows.Count) rows" }
    $rows[$index].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 500
}

function Invoke($element) {
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function StatusText($win) {
    return (FindById $win 'StatusText').Current.Name
}
