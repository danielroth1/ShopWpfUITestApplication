# Podman helpers used by the database and cleanup gates. PowerShell 5.1 compatible.

function Test-PodmanReady {
    param([hashtable]$Context)

    & $Context.PodmanPath ps -a *> $null
    return ($LASTEXITCODE -eq 0)
}

function Start-PodmanIfNeeded {
    param([hashtable]$Context)

    if (Test-PodmanReady -Context $Context) {
        return
    }

    Write-Host '    Podman is not reachable, starting the machine...'
    & $Context.PodmanPath machine start *> $null

    for ($i = 0; $i -lt 60; $i++) {
        if (Test-PodmanReady -Context $Context) {
            return
        }

        Start-Sleep -Seconds 2
    }

    throw 'Podman machine did not become ready within 120 seconds.'
}

function Reset-ShopContainer {
    param([hashtable]$Context)

    if ($Context.KeepContainer) {
        & $Context.PodmanPath start $Context.ContainerName *> $null
        return
    }

    & $Context.PodmanPath rm -f $Context.ContainerName *> $null

    $portMapping = "{0}:{1}:5432" -f $Context.DbHost, $Context.DbPort

    & $Context.PodmanPath run -d `
        --name $Context.ContainerName `
        -p $portMapping `
        -e "POSTGRES_USER=$($Context.DbUser)" `
        -e "POSTGRES_PASSWORD=$($Context.DbPassword)" `
        -e "POSTGRES_DB=$($Context.DbName)" `
        $Context.Image *> $null

    if ($LASTEXITCODE -ne 0) {
        throw 'Failed to start the PostgreSQL container.'
    }
}

function Wait-PostgresReady {
    param([hashtable]$Context)

    for ($i = 0; $i -lt 90; $i++) {
        & $Context.PodmanPath exec $Context.ContainerName pg_isready -U $Context.DbUser -d $Context.DbName *> $null
        if ($LASTEXITCODE -eq 0) {
            return
        }

        Start-Sleep -Seconds 1
    }

    throw 'PostgreSQL did not accept connections within 90 seconds.'
}

function Stop-ShopContainer {
    param([hashtable]$Context)

    & $Context.PodmanPath stop $Context.ContainerName *> $null
}
