#Requires -Version 5.1
<#
.SYNOPSIS
    Idempotent Windows-MCP setup: installs the package, generates an auth key,
    registers the background task and stores the token for VS Code.

.DESCRIPTION
    Safe to re-run. Prints the bearer token and the .vscode\mcp.json snippet.
    After the first run, restart VS Code so the extension host picks up
    WINDOWS_MCP_AUTH_KEY.
#>
[CmdletBinding()]
param(
    [string]$Python = 'C:\Users\daniel\AppData\Local\Programs\Python\Python315\python.exe',
    [string]$Scripts = 'C:\Users\daniel\AppData\Local\Programs\Python\Python315\Scripts',
    [string]$HostAddress = '127.0.0.1',
    [int]$Port = 8000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Python)) {
    throw "Python interpreter not found at '$Python'. Pass -Python <path>."
}

$exe = Join-Path $Scripts 'windows-mcp.exe'
$configPath = Join-Path $env:USERPROFILE '.windows-mcp\config.toml'

Write-Host '==> Installing/upgrading windows-mcp' -ForegroundColor Cyan
& $Python -m pip install --upgrade --quiet windows-mcp
if ($LASTEXITCODE -ne 0) {
    throw 'pip install windows-mcp failed.'
}

if (-not (Test-Path -LiteralPath $exe)) {
    throw "Expected console script at '$exe'."
}

$key = $null
if (Test-Path -LiteralPath $configPath) {
    $match = Select-String -LiteralPath $configPath -Pattern '^\s*auth_key\s*=\s*"([^"]+)"' | Select-Object -First 1
    if ($match) {
        $key = $match.Matches[0].Groups[1].Value
        Write-Host '==> Reusing existing auth key from config.toml' -ForegroundColor Cyan
    }
}

if ([string]::IsNullOrWhiteSpace($key)) {
    Write-Host '==> Generating auth key' -ForegroundColor Cyan
    $output = & $exe auth --transport streamable-http --host $HostAddress --port $Port 2>&1
    $output | Out-Host

    $match = $output | Select-String -Pattern 'Generated auth key:\s*(\S+)' | Select-Object -First 1
    if ($match) {
        $key = $match.Matches[0].Groups[1].Value
    }
}

if ([string]::IsNullOrWhiteSpace($key)) {
    throw 'Could not determine the auth key. Run: windows-mcp auth --transport streamable-http'
}

Write-Host '==> Registering background task' -ForegroundColor Cyan
& $exe install | Out-Host

[Environment]::SetEnvironmentVariable('WINDOWS_MCP_AUTH_KEY', $key, 'User')
[Environment]::SetEnvironmentVariable('WINDOWS_MCP_AUTH_KEY', $key, 'Process')

$url = "http://{0}:{1}/mcp" -f $HostAddress, $Port

Write-Host ''
Write-Host 'Auth token (also stored as user env var WINDOWS_MCP_AUTH_KEY):' -ForegroundColor Green
Write-Host ("  {0}" -f $key) -ForegroundColor White
Write-Host ''
Write-Host 'VS Code server entry (.vscode\mcp.json):' -ForegroundColor Green
Write-Host ('  { "type": "http", "url": "' + $url + '", "headers": { "Authorization": "Bearer ${env:WINDOWS_MCP_AUTH_KEY}" } }')
Write-Host ''
Write-Host 'Restart VS Code, start the server from the MCP view, then verify a Snapshot tool call.' -ForegroundColor Yellow
