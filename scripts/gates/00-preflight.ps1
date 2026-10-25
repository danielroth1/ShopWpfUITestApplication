# Gate 00 - verify the machine can run the loop at all.

function Invoke-GatePreflight {
    param([hashtable]$Context)

    $problems = New-Object System.Collections.ArrayList
    $summary = New-Object System.Collections.ArrayList

    $dotnetVersion = (& $Context.DotnetPath --version 2>&1) -join ''
    if ($LASTEXITCODE -ne 0) {
        [void]$problems.Add("dotnet not runnable at '$($Context.DotnetPath)'")
    }
    elseif (-not $dotnetVersion.StartsWith('10.')) {
        [void]$problems.Add("expected .NET SDK 10 but found '$dotnetVersion'")
    }
    else {
        [void]$summary.Add("dotnet $dotnetVersion")
    }

    $efVersion = (& $Context.DotnetPath ef --version 2>&1) -join ''
    if ($LASTEXITCODE -ne 0) {
        [void]$problems.Add('dotnet-ef is not installed (dotnet tool install --global dotnet-ef --version 10.*)')
    }
    else {
        [void]$summary.Add("dotnet-ef $efVersion")
    }

    # `dotnet ef` shells out to `<muxer> msbuild`, resolved from PATH, so the SDK named by
    # global.json must resolve for a child muxer and not just for the resolved dotnet path.
    Push-Location $Context.Root
    try {
        $sdkProbe = (& $Context.DotnetPath msbuild -version 2>&1) -join ' '
        $sdkProbeExit = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($sdkProbeExit -ne 0) {
        $sdkProbe = ($sdkProbe -replace '\s+', ' ').Trim()
        [void]$problems.Add("no SDK resolves for global.json in '$($Context.Root)' (put the .NET 10 install first on PATH, or set SHOP_DOTNET): $sdkProbe")
    }

    if (-not (Test-Path -LiteralPath $Context.PodmanPath)) {
        $podmanOnPath = Get-Command podman -ErrorAction SilentlyContinue
        if (-not $podmanOnPath) {
            [void]$problems.Add("podman not found at '$($Context.PodmanPath)'")
        }
    }

    try {
        Start-PodmanIfNeeded -Context $Context
        $podmanVersion = (& $Context.PodmanPath --version 2>&1) -join ''
        [void]$summary.Add($podmanVersion.Trim())
    }
    catch {
        [void]$problems.Add($_.Exception.Message)
    }

    $pythonVersion = (& $Context.PythonPath --version 2>&1) -join ''
    if ($LASTEXITCODE -ne 0) {
        [void]$problems.Add("python not runnable at '$($Context.PythonPath)'")
    }
    else {
        [void]$summary.Add($pythonVersion.Trim())
    }

    if (-not (Test-Path -LiteralPath $Context.WindowsMcpPath)) {
        [void]$problems.Add("windows-mcp not installed (python -m pip install windows-mcp); looked for '$($Context.WindowsMcpPath)'")
    }

    $mcpKey = Get-WindowsMcpKey
    if ([string]::IsNullOrWhiteSpace($mcpKey)) {
        [void]$problems.Add('no Windows-MCP auth key: run scripts\setup-mcp.ps1')
    }
    elseif (Test-McpEndpoint -Url $Context.McpUrl -Key $mcpKey) {
        [void]$summary.Add("MCP reachable at $($Context.McpUrl)")
    }
    else {
        [void]$problems.Add("MCP endpoint $($Context.McpUrl) did not accept an initialize request (start the windows-mcp-server task)")
    }

    if ($problems.Count -gt 0) {
        throw ("Preflight failed:`n - " + ($problems -join "`n - "))
    }

    return ($summary -join ' | ')
}
