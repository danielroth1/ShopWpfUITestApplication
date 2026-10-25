# Gate 30 - run the database-backed integration tests.

function Invoke-GateTests {
    param([hashtable]$Context)

    $env:SHOP_DB_CONNECTION = $Context.ConnectionString
    $resultsDir = $Context.IterationDir

    Push-Location $Context.Root
    try {
        & $Context.DotnetPath test ShopTest.slnx --nologo -v minimal `
            --results-directory $resultsDir `
            --logger "trx;LogFileName=integration-tests.trx"
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($exitCode -ne 0) {
        throw "dotnet test failed with exit code $exitCode (see integration-tests.trx)"
    }

    return 'integration tests green'
}
