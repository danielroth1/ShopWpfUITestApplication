# Gate 10 - compile the solution with warnings treated as errors.

function Invoke-GateBuild {
    param([hashtable]$Context)

    Push-Location $Context.Root
    try {
        & $Context.DotnetPath build ShopTest.slnx -warnaserror --nologo -v minimal
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($exitCode -ne 0) {
        throw "dotnet build failed with exit code $exitCode (see build log)"
    }

    return 'solution builds clean with warnings as errors'
}
