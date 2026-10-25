# Shared helpers for the gauntlet gates. PowerShell 5.1 compatible.

Set-StrictMode -Version Latest

function Resolve-ToolPath {
    param(
        [string]$Explicit,
        [string[]]$Candidates,
        [string]$Fallback
    )

    if ($Explicit -and (Test-Path -LiteralPath $Explicit)) {
        return $Explicit
    }

    foreach ($candidate in $Candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            return $candidate
        }
    }

    return $Fallback
}

function Import-DotEnv {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    foreach ($line in Get-Content -LiteralPath $Path) {
        $trimmed = $line.Trim()
        if ($trimmed.Length -eq 0 -or $trimmed.StartsWith('#')) {
            continue
        }

        $parts = $trimmed.Split('=', 2)
        if ($parts.Length -ne 2) {
            continue
        }

        $name = $parts[0].Trim()
        $value = $parts[1].Trim()

        if (-not [Environment]::GetEnvironmentVariable($name, 'Process')) {
            [Environment]::SetEnvironmentVariable($name, $value, 'Process')
        }
    }
}

function Get-EnvOrDefault {
    param([string]$Name, [string]$Default)

    $value = [Environment]::GetEnvironmentVariable($Name, 'Process')
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $Default
    }

    return $value
}

function New-GauntletContext {
    param(
        [string]$Root,
        [int]$Iteration
    )

    $artifactsRoot = Join-Path $Root 'artifacts\gauntlet'
    $iterationDir = Join-Path $artifactsRoot ("iter-{0}" -f $Iteration)
    New-Item -ItemType Directory -Force -Path $iterationDir | Out-Null

    $dotnet = Resolve-ToolPath `
        -Explicit $env:SHOP_DOTNET `
        -Candidates @(
            'C:\Users\daniel\.dotnet10\dotnet.exe',
            'C:\Program Files\dotnet\dotnet.exe'
        ) `
        -Fallback 'dotnet'

    $podman = Resolve-ToolPath `
        -Explicit $env:SHOP_PODMAN `
        -Candidates @(
            'C:\Users\daniel\AppData\Local\Programs\Podman\podman.exe'
        ) `
        -Fallback 'podman'

    $windowsMcp = Resolve-ToolPath `
        -Explicit $env:SHOP_WINDOWS_MCP `
        -Candidates @(
            'C:\Users\daniel\AppData\Local\Programs\Python\Python315\Scripts\windows-mcp.exe'
        ) `
        -Fallback 'windows-mcp'

    $python = Resolve-ToolPath `
        -Explicit $env:SHOP_PYTHON `
        -Candidates @(
            'C:\Users\daniel\AppData\Local\Programs\Python\Python315\python.exe'
        ) `
        -Fallback 'python'

    $git = Resolve-ToolPath `
        -Explicit $env:SHOP_GIT `
        -Candidates @(
            'C:\Program Files\Git\cmd\git.exe',
            'C:\Program Files (x86)\Git\cmd\git.exe'
        ) `
        -Fallback 'git'

    $dbHost = Get-EnvOrDefault 'SHOP_DB_HOST' '127.0.0.1'
    $dbPort = Get-EnvOrDefault 'SHOP_DB_PORT' '55432'
    $dbName = Get-EnvOrDefault 'SHOP_DB_NAME' 'shop'
    $dbUser = Get-EnvOrDefault 'SHOP_DB_USER' 'shop'
    $dbPassword = Get-EnvOrDefault 'SHOP_DB_PASSWORD' 'shop'

    $connectionString = Get-EnvOrDefault 'SHOP_DB_CONNECTION' ("Host={0};Port={1};Database={2};Username={3};Password={4};Include Error Detail=true" -f $dbHost, $dbPort, $dbName, $dbUser, $dbPassword)

    return @{
        Root              = $Root
        Iteration         = $Iteration
        ArtifactsRoot     = $artifactsRoot
        IterationDir      = $iterationDir
        ReportPath        = Join-Path $iterationDir 'report.json'
        DotnetPath        = $dotnet
        PodmanPath        = $podman
        WindowsMcpPath    = $windowsMcp
        PythonPath        = $python
        GitPath           = $git
        CommitEnabled     = $true
        GitInfo           = $null
        ContainerName     = Get-EnvOrDefault 'SHOP_PG_CONTAINER' 'shop-pg'
        Image             = Get-EnvOrDefault 'SHOP_PG_IMAGE' 'postgres:17-alpine'
        DbHost            = $dbHost
        DbPort            = $dbPort
        DbName            = $dbName
        DbUser            = $dbUser
        DbPassword        = $dbPassword
        ConnectionString  = $connectionString
        McpUrl            = Get-EnvOrDefault 'WINDOWS_MCP_URL' 'http://127.0.0.1:8000/mcp'
        AppExe            = Join-Path $Root 'src\ShopTest.App\bin\Debug\net10.0-windows\ShopTest.App.exe'
        GateResults       = @()
        KeepContainer     = $false
        StopContainer     = $false
    }
}

function Get-WindowsMcpKey {
    $key = [Environment]::GetEnvironmentVariable('WINDOWS_MCP_AUTH_KEY', 'Process')
    if ([string]::IsNullOrWhiteSpace($key)) {
        $key = [Environment]::GetEnvironmentVariable('WINDOWS_MCP_AUTH_KEY', 'User')
    }

    if (-not [string]::IsNullOrWhiteSpace($key)) {
        return $key
    }

    $configPath = Join-Path $env:USERPROFILE '.windows-mcp\config.toml'
    if (Test-Path -LiteralPath $configPath) {
        $match = Select-String -LiteralPath $configPath -Pattern '^\s*auth_key\s*=\s*"([^"]+)"' | Select-Object -First 1
        if ($match) {
            return $match.Matches[0].Groups[1].Value
        }
    }

    return $null
}

function Test-McpEndpoint {
    param(
        [string]$Url,
        [string]$Key
    )

    if ([string]::IsNullOrWhiteSpace($Key)) {
        return $false
    }

    $body = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"gauntlet","version":"1.0"}}}'

    try {
        $response = Invoke-WebRequest `
            -Uri $Url `
            -Method Post `
            -Headers @{ Authorization = "Bearer $Key"; Accept = 'application/json, text/event-stream' } `
            -ContentType 'application/json' `
            -Body $body `
            -UseBasicParsing `
            -TimeoutSec 15

        return ($response.StatusCode -eq 200)
    }
    catch {
        return $false
    }
}

function Invoke-GauntletGate {
    param(
        [hashtable]$Context,
        [string]$Id,
        [string]$Title,
        [scriptblock]$Body
    )

    $logPath = Join-Path $Context.IterationDir ("{0}.log" -f $Id)
    $started = Get-Date
    $status = 'pass'
    $detail = ''

    Write-Host ''
    Write-Host ("==> {0} - {1}" -f $Id, $Title) -ForegroundColor Cyan

    try {
        $output = & $Body $Context 2>&1
        if ($null -ne $output) {
            $detail = (($output | Out-String).Trim())
        }

        $detail | Set-Content -LiteralPath $logPath
        $firstLines = ($detail -split "`r?`n") | Select-Object -First 3
        Write-Host ("    " + ($firstLines -join ' ')) -ForegroundColor DarkGray
    }
    catch {
        $status = 'fail'
        $detail = $_.Exception.Message
        (($_ | Out-String) + "`n" + $detail) | Set-Content -LiteralPath $logPath
        Write-Host ("    FAILED: {0}" -f $detail) -ForegroundColor Red
    }

    $duration = [int]((Get-Date) - $started).TotalMilliseconds

    $entry = [pscustomobject]@{
        id         = $Id
        title      = $Title
        status     = $status
        durationMs = $duration
        detail     = $detail
        log        = $logPath
    }

    $Context.GateResults += $entry
    return $entry
}

function Save-GauntletReport {
    param(
        [hashtable]$Context,
        [datetime]$StartedAt
    )

    $failed = @($Context.GateResults | Where-Object { $_.status -ne 'pass' })

    $gitInfo = $null
    if ($null -ne $Context.GitInfo) {
        $gitInfo = [pscustomobject]$Context.GitInfo
    }

    $report = [pscustomobject]@{
        iteration  = $Context.Iteration
        startedAt  = $StartedAt.ToString('o')
        finishedAt = (Get-Date).ToString('o')
        result     = $(if ($failed.Count -eq 0) { 'pass' } else { 'fail' })
        git        = $gitInfo
        gates      = @($Context.GateResults)
    }

    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $Context.ReportPath

    Write-Host ''
    Write-Host ("Report: {0}" -f $Context.ReportPath) -ForegroundColor DarkGray

    foreach ($gate in $Context.GateResults) {
        $color = if ($gate.status -eq 'pass') { 'Green' } else { 'Red' }
        Write-Host ("  {0,-12} {1,-6} {2,6} ms" -f $gate.id, $gate.status, $gate.durationMs) -ForegroundColor $color
    }

    return ($failed.Count -eq 0)
}
