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

.EXAMPLE
    .\scripts\gauntlet.ps1 -Rollback
    Discards the current working tree and returns to the last green checkpoint.
#>
[CmdletBinding()]
param(
    [ValidateSet('all', 'preflight', 'build', 'database', 'tests', 'cleanup')]
    [string]$Gate = 'all',

    [int]$Iteration = 1,

    [switch]$KeepContainer,

    [switch]$StopContainer,

    [switch]$NoCommit,

    [switch]$Rollback,

    [string]$RollbackTo
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptRoot = $PSScriptRoot
$repoRoot = Split-Path -Parent $scriptRoot

. (Join-Path $scriptRoot 'lib\common.ps1')
. (Join-Path $scriptRoot 'lib\podman.ps1')
. (Join-Path $scriptRoot 'lib\git.ps1')
. (Join-Path $scriptRoot 'gates\00-preflight.ps1')
. (Join-Path $scriptRoot 'gates\10-build.ps1')
. (Join-Path $scriptRoot 'gates\20-database.ps1')
. (Join-Path $scriptRoot 'gates\30-tests.ps1')
. (Join-Path $scriptRoot 'gates\90-cleanup.ps1')

Import-DotEnv (Join-Path $repoRoot '.env')

$context = New-GauntletContext -Root $repoRoot -Iteration $Iteration
$context.KeepContainer = [bool]$KeepContainer
$context.StopContainer = [bool]$StopContainer
$context.CommitEnabled = (-not $NoCommit)

if ($Rollback -or $RollbackTo) {
    Initialize-GitRepository -Context $context
    Ensure-GitIdentity -Context $context

    $target = $RollbackTo
    if ([string]::IsNullOrWhiteSpace($target)) {
        $target = Get-LatestGreenTag -Context $context
    }

    if ([string]::IsNullOrWhiteSpace($target)) {
        throw 'No green checkpoint exists yet. Pass -RollbackTo <tag-or-commit>.'
    }

    $sha = Restore-GitCheckpoint -Context $context -Ref $target
    Write-Host ''
    Write-Host ("Rolled back to '{0}' ({1})" -f $target, (Get-GitShortSha -Sha $sha)) -ForegroundColor Green
    Write-Host 'Artifacts under artifacts\gauntlet were kept (ignored by git).' -ForegroundColor DarkGray
    exit 0
}

Write-Host ''
Write-Host ("ShopTest gauntlet - iteration {0} - gate '{1}'" -f $Iteration, $Gate) -ForegroundColor White
Write-Host ("Artifacts: {0}" -f $context.IterationDir) -ForegroundColor DarkGray

$startedAt = Get-Date

$commitLoop = ($Gate -eq 'all' -and $context.CommitEnabled)
$baseline = $null
$baselineTag = "gauntlet/iter-{0}-start" -f $Iteration

if ($commitLoop) {
    $baseline = New-GitCheckpoint -Context $context -Message ("gauntlet: iteration {0} baseline" -f $Iteration)
    Set-GitCheckpointTag -Context $context -Tag $baselineTag -Sha $baseline.Sha -Message ("Gauntlet iteration {0} starting point" -f $Iteration)

    $baselineNote = 'working tree already clean'
    if ($baseline.Committed) {
        $baselineNote = 'uncommitted work captured'
    }

    Write-Host ("Checkpoint: {0} at {1} ({2})" -f $baselineTag, (Get-GitShortSha -Sha $baseline.Sha), $baselineNote) -ForegroundColor DarkGray
}

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

$failedGate = $context.GateResults | Where-Object { $_.status -ne 'pass' } | Select-Object -First 1
$allPassed = ($null -eq $failedGate)

if ($commitLoop) {
    if ($allPassed) {
        $resultTag = "gauntlet/iter-{0}-green" -f $Iteration
        $result = New-GitCheckpoint -Context $context -Message ("gauntlet: iteration {0} green" -f $Iteration)
    }
    else {
        $resultTag = "gauntlet/iter-{0}-failed" -f $Iteration
        $result = New-GitCheckpoint -Context $context -Message ("gauntlet: iteration {0} failed ({1})" -f $Iteration, $failedGate.id)
    }

    Set-GitCheckpointTag -Context $context -Tag $resultTag -Sha $result.Sha -Message ("Gauntlet iteration {0}: {1}" -f $Iteration, $(if ($allPassed) { 'green' } else { 'failed' }))

    $context.GitInfo = @{
        enabled      = $true
        baseline     = $baseline.Sha
        baselineTag  = $baselineTag
        result       = $result.Sha
        resultTag    = $resultTag
        resultStatus = $(if ($allPassed) { 'green' } else { 'failed' })
        committed    = $result.Committed
    }

    Write-Host ("Checkpoint: {0} at {1}" -f $resultTag, (Get-GitShortSha -Sha $result.Sha)) -ForegroundColor DarkGray
}

$passed = Save-GauntletReport -Context $context -StartedAt $startedAt

if ($Gate -ne 'cleanup') {
    Write-Host ''
    Write-Host 'UI gates (app launch + scenario replay) are executed by the gauntlet agent through Windows-MCP.' -ForegroundColor DarkGray
}

if (-not $passed) {
    Write-Host ''
    Write-Host ("Discard this iteration: scripts\gauntlet.ps1 -Rollback -Iteration {0}" -f $Iteration) -ForegroundColor Yellow

    if ($commitLoop) {
        Write-Host ("Inspect what was tried:  {0} show {1}" -f $context.GitPath, (Get-GitShortSha -Sha $context.GitInfo.result)) -ForegroundColor DarkGray
    }

    exit 1
}

exit 0
