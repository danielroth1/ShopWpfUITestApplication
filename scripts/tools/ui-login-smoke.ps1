#Requires -Version 5.1
<#
.SYNOPSIS
    Drives a real sign-in through UI Automation and verifies the catalog screen appears.

.DESCRIPTION
    This is a local smoke check for the same accessibility contract the Windows-MCP scenarios rely on.
    It does not replace the gauntlet UI gate; it is a fast way to prove the app is testable by hand.

    The app must already be running on the login screen:
        powershell -NoProfile -ExecutionPolicy Bypass -File scripts\tools\ui-login-smoke.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('client', 'admin')]
    [string]$Role = 'client',
    [string]$ProcessName = 'ShopTest.App',
    [int]$TimeoutSeconds = 25,
    [switch]$AddItemToCart
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

function Get-AppWindow {
    param([int]$ProcessId)

    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
        $ProcessId)

    return $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
}

function Find-ById {
    param($Window, [string]$AutomationId)

    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        $AutomationId)

    return $Window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Wait-ById {
    param([int]$ProcessId, [string]$AutomationId, [int]$TimeoutSeconds)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)

    while ((Get-Date) -lt $deadline) {
        $window = Get-AppWindow -ProcessId $ProcessId
        if ($window) {
            $element = Find-ById -Window $window -AutomationId $AutomationId
            if ($element) {
                return $element
            }
        }

        Start-Sleep -Milliseconds 400
    }

    throw "Element '$AutomationId' did not appear within $TimeoutSeconds seconds."
}

function Set-ElementValue {
    param($Element, [string]$Value)

    $pattern = $Element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $pattern.SetValue($Value)
}

function Invoke-Element {
    param($Element)

    $pattern = $Element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
}

$process = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $process) {
    throw "Process '$ProcessName' is not running. Start the app first."
}

Write-Host '==> Signing in through UI Automation' -ForegroundColor Cyan

if ($Role -eq 'admin') {
    $demoEmail = 'admin@shop.local'
    $demoSecret = 'Admin123!'
}
else {
    $demoEmail = 'client@shop.local'
    $demoSecret = 'Client123!'
}

$emailBox = Wait-ById -ProcessId $process.Id -AutomationId 'auth.email' -TimeoutSeconds 10
Set-ElementValue -Element $emailBox -Value $demoEmail

$passwordBox = Wait-ById -ProcessId $process.Id -AutomationId 'auth.password' -TimeoutSeconds 10
Set-ElementValue -Element $passwordBox -Value $demoSecret

$submit = Find-ById -Window (Get-AppWindow -ProcessId $process.Id) -AutomationId 'auth.submit'
Invoke-Element -Element $submit

Write-Host '==> Waiting for the catalog' -ForegroundColor Cyan
$null = Wait-ById -ProcessId $process.Id -AutomationId 'catalog.search' -TimeoutSeconds $TimeoutSeconds

$window = Get-AppWindow -ProcessId $process.Id
$checks = @('catalog.search', 'catalog.searchButton', 'catalog.clearButton', 'catalog.categoryList', 'catalog.items', 'shell.cartButton', 'shell.burgerButton')
$missing = 0

foreach ($id in $checks) {
    $element = Find-ById -Window $window -AutomationId $id
    if ($element) {
        Write-Host ("  [ok]   {0,-22} -> '{1}'" -f $id, $element.Current.Name) -ForegroundColor Green
    }
    else {
        Write-Host ("  [MISS] {0}" -f $id) -ForegroundColor Red
        $missing++
    }
}

$item = Find-ById -Window $window -AutomationId 'item.7.add'
if ($item) {
    Write-Host ("  [ok]   item.7.add             -> '{0}'" -f $item.Current.Name) -ForegroundColor Green
}
else {
    Write-Host '  [info] item.7.add not found - card ids depend on seeded item ids' -ForegroundColor Yellow
}

if ($missing -gt 0) {
    exit 1
}

if ($AddItemToCart) {
    Write-Host '==> Adding the first catalog item to the cart' -ForegroundColor Cyan

    $all = $window.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)

    $addButton = $all | Where-Object { $_.Current.AutomationId -match '^item\.\d+\.add$' } | Select-Object -First 1
    if (-not $addButton) {
        throw 'No add-to-cart button found on the catalog screen.'
    }

    Invoke-Element -Element $addButton
    Start-Sleep -Milliseconds 1500

    $cartButton = Find-ById -Window (Get-AppWindow -ProcessId $process.Id) -AutomationId 'shell.cartButton'
    Write-Host ("  cart button accessible name: '{0}'" -f $cartButton.Current.Name)

    if ($cartButton.Current.Name -notmatch 'Cart \(\d+\)') {
        throw 'The cart button does not expose the item count in its accessible name.'
    }
}

Write-Host 'Catalog reached and interactive controls are addressable.' -ForegroundColor Green
exit 0
