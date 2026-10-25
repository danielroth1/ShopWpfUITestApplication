#Requires -Version 5.1
<#
.SYNOPSIS
    Starts or stops the ShopTest PostgreSQL container without the gauntlet runner.

.DESCRIPTION
    The app applies migrations and seeds demo data on startup, so the container is the only
    prerequisite for running it. `start` is idempotent: it starts the Podman machine if needed,
    starts an existing container, or creates the container on first use.

.PARAMETER Action
    start  - start (or create) the container and wait until PostgreSQL accepts connections
    stop   - stop the container
    status - print the container state
    remove - delete the container (all data is throwaway)

.EXAMPLE
    .\scripts\tools\shop-db.ps1 -Action start
#>
[CmdletBinding()]
param(
    [ValidateSet('start', 'stop', 'status', 'remove')]
    [string]$Action = 'start'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $repoRoot 'scripts\lib\common.ps1')
Import-DotEnv (Join-Path $repoRoot '.env')

$podman = Resolve-ToolPath `
    -Explicit $env:SHOP_PODMAN `
    -Candidates @(
    'C:\Users\daniel\AppData\Local\Programs\Podman\podman.exe'
) `
    -Fallback 'podman'

$container = Get-EnvOrDefault 'SHOP_PG_CONTAINER' 'shop-pg'
$image = Get-EnvOrDefault 'SHOP_PG_IMAGE' 'postgres:17-alpine'
$dbHost = Get-EnvOrDefault 'SHOP_DB_HOST' '127.0.0.1'
$dbPort = Get-EnvOrDefault 'SHOP_DB_PORT' '55432'
$dbName = Get-EnvOrDefault 'SHOP_DB_NAME' 'shop'
$dbUser = Get-EnvOrDefault 'SHOP_DB_USER' 'shop'
$dbPassword = Get-EnvOrDefault 'SHOP_DB_PASSWORD' 'shop'

function Test-PodmanReady {
    try {
        & $podman ps -a *> $null
        return ($LASTEXITCODE -eq 0)
    }
    catch {
        return $false
    }
}

# Starts the Podman machine once when it is not reachable.
function Start-PodmanIfNeeded {
    if (Test-PodmanReady) {
        return
    }

    Write-Host 'Podman is not reachable, starting the machine...'
    & $podman machine start *> $null

    for ($i = 0; $i -lt 60; $i++) {
        if (Test-PodmanReady) {
            return
        }

        Start-Sleep -Seconds 2
    }

    throw "Podman machine did not become ready within 120 seconds. Is podman installed at '$podman'?"
}

switch ($Action) {
    'start' {
        Start-PodmanIfNeeded

        & $podman start $container *> $null
        if ($LASTEXITCODE -ne 0) {
            Write-Host ("Creating container '{0}' from {1}..." -f $container, $image)
            & $podman run -d `
                --name $container `
                -p "${dbHost}:${dbPort}:5432" `
                -e "POSTGRES_USER=$dbUser" `
                -e "POSTGRES_PASSWORD=$dbPassword" `
                -e "POSTGRES_DB=$dbName" `
                $image *> $null

            if ($LASTEXITCODE -ne 0) {
                throw "Failed to create container '$container'."
            }
        }

        for ($i = 0; $i -lt 90; $i++) {
            & $podman exec $container pg_isready -U $dbUser -d $dbName *> $null
            if ($LASTEXITCODE -eq 0) {
                Write-Host ("PostgreSQL ready on {0}:{1} (container '{2}')." -f $dbHost, $dbPort, $container)
                Write-Host 'The app migrates and seeds on startup: dotnet run --project src\ShopTest.App'
                exit 0
            }

            Start-Sleep -Seconds 1
        }

        throw 'PostgreSQL did not accept connections within 90 seconds.'
    }
    'stop' {
        Start-PodmanIfNeeded
        & $podman stop $container *> $null
        Write-Host ("Container '{0}' stopped." -f $container)
    }
    'status' {
        Start-PodmanIfNeeded
        & $podman ps -a --filter "name=^${container}$" --format '{{.Names}} {{.Status}} {{.Ports}}'
    }
    'remove' {
        Start-PodmanIfNeeded
        & $podman rm -f $container *> $null
        Write-Host ("Container '{0}' removed." -f $container)
    }
}
