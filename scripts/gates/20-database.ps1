# Gate 20 - recreate the PostgreSQL container and apply migrations.

function Invoke-GateDatabase {
    param([hashtable]$Context)

    Start-PodmanIfNeeded -Context $Context
    Reset-ShopContainer -Context $Context
    Wait-PostgresReady -Context $Context

    $env:SHOP_DB_CONNECTION = $Context.ConnectionString

    Push-Location $Context.Root
    try {
        & $Context.DotnetPath ef database update --project src\ShopTest.Core --connection $Context.ConnectionString
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($exitCode -ne 0) {
        throw "dotnet ef database update failed with exit code $exitCode"
    }

    return ("database '{0}' migrated on {1}:{2}" -f $Context.DbName, $Context.DbHost, $Context.DbPort)
}
