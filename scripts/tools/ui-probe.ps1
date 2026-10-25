#Requires -Version 5.1
<#
.SYNOPSIS
    Probes the running ShopTest app through UI Automation and reports which AutomationIds are visible.

.DESCRIPTION
    A quick local sanity check for the same contract Windows-MCP relies on. Run the app first, then:
        powershell -NoProfile -ExecutionPolicy Bypass -File scripts\tools\ui-probe.ps1

    Pass -Ids to check a specific set of AutomationIds.
#>
[CmdletBinding()]
param(
    [string[]]$Ids = @('auth.email', 'auth.password', 'auth.submit', 'auth.toggleMode', 'auth.demoHint'),
    [string]$ProcessName = 'ShopTest.App'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$process = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $process) {
    throw "Process '$ProcessName' is not running. Start the app first."
}

$root = [System.Windows.Automation.AutomationElement]::RootElement
$condition = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
    $process.Id)

$window = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
if (-not $window) {
    throw "No top-level window found for process $($process.Id)."
}

Write-Host ("window '{0}' id='{1}'" -f $window.Current.Name, $window.Current.AutomationId)

$missing = 0

foreach ($id in $Ids) {
    $idCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        $id)

    $element = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $idCondition)

    if ($element) {
        Write-Host ("  [ok]   {0,-24} -> '{1}'" -f $id, $element.Current.Name) -ForegroundColor Green
    }
    else {
        Write-Host ("  [MISS] {0}" -f $id) -ForegroundColor Red
        $missing++
    }
}

if ($missing -gt 0) {
    exit 1
}

exit 0
