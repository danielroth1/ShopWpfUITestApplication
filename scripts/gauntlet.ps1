#Requires -Version 5.1
<#
.SYNOPSIS
    Deterministic gate runner for the ShopTest gauntlet loop.

.DESCRIPTION
    Runs the machine-checkable gates and writes a per-iteration report to
    artifacts\gauntlet\iter-<n>\report.json. UI gates (app launch, scenario replay)
    are executed by the gauntlet agent through Windows-MCP, not by this script.

.EXAMPLE
    .\scripts\gauntlet.ps1 -Gate all -Iteration 1

.EXAMPLE
    .\scripts\gauntlet.ps1 -Gate tests -KeepContainer
#>
[CmdletBinding()]
param(
    [ValidateSet('all', 'preflight', 'build', 'database', 'tests', 'cleanup')]
    [string]$Gate = 'all',

    [int]$Iteration = 1,

    [switch]$KeepContainer,

    [switch]$StopContainer
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptRoot = $PSScriptRoot
$repoRoot = Split-Path -Parent $scriptRoot

. (Join-Path $scriptRoot 'lib\common.ps1')
. (Join-Path $scriptRoot 'lib\podman.ps1')
. (Join-Path $scriptRoot 'gates\00-preflight.ps1')
. (Join-Path $scriptRoot 'gates\10-build.ps1')
. (Join-Path $scriptRoot 'gates\20-database.ps1')
. (Join-Path $scriptRoot 'gates\30-tests.ps1')
. (Join-Path $scriptRoot 'gates\90-cleanup.ps1')

Import-DotEnv (Join-Path $repoRoot '.env')

$context = New-GauntletContext -Root $repoRoot -Iteration $Iteration
$context.KeepContainer = [bool]$KeepContainer
$context.StopContainer = [bool]$StopContainer

Write-Host ''
Write-Host ("ShopTest gauntlet - iteration {0} - gate '{1}'" -f $Iteration, $Gate) -ForegroundColor White
Write-Host ("Artifacts: {0}" -f $context.IterationDir) -ForegroundColor DarkGray

$startedAt = Get-Date

$allGates = @(
    [pscustomobject]@{
        Id     = 'preflight'
        Title  = 'Toolchain, Podman and Windows-MCP preflight'
        Action = { param($ctx) Invoke-GatePreflight -Context $ctx }
    },
    [pscustomobject]@{
        Id     = 'build'
        Title  = 'Build solution with warnings as errors'
        Action = { param($ctx) Invoke-GateBuild -Context $ctx }
    },
    [pscustomobject]@{
        Id     = 'database'
        Title  = 'Recreate PostgreSQL container and apply migrations'
        Action = { param($ctx) Invoke-GateDatabase -Context $ctx }
    },
    [pscustomobject]@{
        Id     = 'tests'
        Title  = 'Run integration tests'
        Action = { param($ctx) Invoke-GateTests -Context $ctx }
    },
    [pscustomobject]@{
        Id     = 'cleanup'
        Title  = 'Stop app processes and container'
        Action = { param($ctx) Invoke-GateCleanup -Context $ctx }
    }
)

if ($Gate -eq 'all') {
    $plan = $allGates
}
else {
    $plan = $allGates | Where-Object { $_.Id -eq $Gate }
}

$failed = $false

foreach ($step in $plan) {
    $result = Invoke-GauntletGate -Context $context -Id $step.Id -Title $step.Title -Body $step.Action

    if ($result.status -ne 'pass' -and $step.Id -ne 'cleanup') {
        $failed = $true
        break
    }
}

if ($failed -and $Gate -eq 'all') {
    Write-Host ''
    Write-Host 'A gate failed - running cleanup before reporting.' -ForegroundColor Yellow
    $cleanup = $allGates | Where-Object { $_.Id -eq 'cleanup' }
    Invoke-GauntletGate -Context $context -Id $cleanup.Id -Title $cleanup.Title -Body $cleanup.Action | Out-Null
}

$passed = Save-GauntletReport -Context $context -StartedAt $startedAt

if ($Gate -ne 'cleanup') {
    Write-Host ''
    Write-Host 'UI gates (app launch + scenario replay) are executed by the gauntlet agent through Windows-MCP.' -ForegroundColor DarkGray
}

if (-not $passed) {
    exit 1
}

exit 0
