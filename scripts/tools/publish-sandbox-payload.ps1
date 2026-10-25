#Requires -Version 5.1
<#
.SYNOPSIS
    Publishes the sandbox payload: a self-contained app, the UI runner and the scenario files.

.DESCRIPTION
    Windows Sandbox has no .NET SDK/runtime, so both executables are published self-contained for win-x64.
    The payload is cached: it is only rebuilt when a source file is newer than the stamp file, unless
    -Force is passed.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts\tools\publish-sandbox-payload.ps1 -Force
#>
[CmdletBinding()]
param(
    [string]$OutputRoot,
    [string]$Configuration = 'Release',
    [string]$DotnetPath = 'dotnet',
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot 'artifacts\gauntlet\payload'
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot $OutputRoot
}

$appDirectory = Join-Path $OutputRoot 'app'
$runnerDirectory = Join-Path $OutputRoot 'runner'
$scenarioDirectory = Join-Path $OutputRoot 'scenarios'
$schemaDirectory = Join-Path $OutputRoot 'schema'
$stampPath = Join-Path $OutputRoot '.stamp'

$appExe = Join-Path $appDirectory 'ShopTest.App.exe'
$runnerExe = Join-Path $runnerDirectory 'ShopTest.UiRunner.exe'

function Get-NewestSourceTime {
    $patterns = @(
        (Join-Path $repoRoot 'src\*\*.cs'),
        (Join-Path $repoRoot 'src\*\*.xaml'),
        (Join-Path $repoRoot 'src\*\*.csproj'),
        (Join-Path $repoRoot 'tests\ShopTest.UiRunner\**\*.cs'),
        (Join-Path $repoRoot 'tests\ShopTest.UiRunner\*.csproj'),
        (Join-Path $repoRoot 'ui-tests\scenarios\*.json')
    )

    $newest = [datetime]::MinValue

    foreach ($pattern in $patterns) {
        $files = Get-ChildItem -Path $pattern -File -ErrorAction SilentlyContinue
        foreach ($file in $files) {
            if ($file.LastWriteTime -gt $newest) {
                $newest = $file.LastWriteTime
            }
        }
    }

    foreach ($shared in 'Directory.Build.props', 'Directory.Packages.props', 'global.json') {
        $path = Join-Path $repoRoot $shared
        if (Test-Path -LiteralPath $path) {
            $time = (Get-Item -LiteralPath $path).LastWriteTime
            if ($time -gt $newest) {
                $newest = $time
            }
        }
    }

    return $newest
}

$fresh = (-not $Force) -and (Test-Path -LiteralPath $appExe) -and (Test-Path -LiteralPath $runnerExe) -and (Test-Path -LiteralPath $stampPath)

if ($fresh) {
    $stamp = (Get-Item -LiteralPath $stampPath).LastWriteTime
    $newest = Get-NewestSourceTime
    $fresh = $newest -le $stamp
}

if (-not $fresh) {
    Write-Host ("==> publishing sandbox payload ({0}, win-x64, self-contained)" -f $Configuration) -ForegroundColor Cyan

    foreach ($directory in @($appDirectory, $runnerDirectory, $scenarioDirectory)) {
        if (Test-Path -LiteralPath $directory) {
            Remove-Item -LiteralPath $directory -Recurse -Force
        }
    }

    $common = @(
        '--configuration', $Configuration,
        '--runtime', 'win-x64',
        '--self-contained', 'true',
        '-p:DebugType=None',
        '-p:DebugSymbols=false'
    )

    & $DotnetPath publish (Join-Path $repoRoot 'src\ShopTest.App\ShopTest.App.csproj') @common -o $appDirectory --nologo -v minimal | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish of ShopTest.App failed with exit code $LASTEXITCODE"
    }

    & $DotnetPath publish (Join-Path $repoRoot 'tests\ShopTest.UiRunner\ShopTest.UiRunner.csproj') @common -o $runnerDirectory --nologo -v minimal | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish of ShopTest.UiRunner failed with exit code $LASTEXITCODE"
    }

    New-Item -ItemType Directory -Force -Path $scenarioDirectory | Out-Null
    Copy-Item -Path (Join-Path $repoRoot 'ui-tests\scenarios\*.json') -Destination $scenarioDirectory -Force

    # The runner replays every *.json in the scenarios folder, so the contract schema is kept beside it,
    # never inside it.
    New-Item -ItemType Directory -Force -Path $schemaDirectory | Out-Null
    Copy-Item -Path (Join-Path $repoRoot 'ui-tests\schema\*.json') -Destination $schemaDirectory -Force

    Set-Content -LiteralPath $stampPath -Value (Get-Date -Format 'o')
}
else {
    Write-Host '==> sandbox payload is up to date' -ForegroundColor DarkGray
}

# Older payloads copied the schema into the scenarios folder; drop it so a cached payload still replays.
Get-ChildItem -Path $scenarioDirectory -Filter '*schema*.json' -File -ErrorAction SilentlyContinue |
    Remove-Item -Force

return [pscustomobject]@{
    Root              = $OutputRoot
    AppDirectory      = $appDirectory
    AppExe            = $appExe
    RunnerDirectory   = $runnerDirectory
    RunnerExe         = $runnerExe
    ScenarioDirectory = $scenarioDirectory
}
