#Requires -Version 5.1
<#
.SYNOPSIS
    Capability probe for the Windows Sandbox UI lane.

.DESCRIPTION
    Boots a throwaway sandbox with a probe payload, records what the guest can actually do (screen size,
    DPI, in-guest screen capture, .NET availability, host reachability) and writes the result to
    artifacts\gauntlet\sandbox-probe\<timestamp>\results\probe.json.

    Run this once after enabling Containers-DisposableClientVM. Everything it finds is informational: a
    blank capture disables screenshot steps, it does not block the gate.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts\tools\sandbox-probe.ps1 -NoLaunch
    Reports host capability only, without starting a sandbox.
#>
[CmdletBinding()]
param(
    [int]$Port = 0,
    [int]$MemoryMb = 4096,
    [int]$TimeoutSeconds = 300,
    [switch]$NoLaunch,
    [switch]$KeepWindow
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $repoRoot 'scripts\lib\sandbox.ps1')

if ($Port -le 0) {
    $portValue = [Environment]::GetEnvironmentVariable('SHOP_SANDBOX_PORT', 'Process')
    $Port = if ([string]::IsNullOrWhiteSpace($portValue)) { 55433 } else { [int]$portValue }
}

$probe = Test-WindowsSandboxAvailable

Write-Host ''
Write-Host 'Host capability' -ForegroundColor White
Write-Host ("  edition        {0}" -f $probe.Edition)
Write-Host ("  build          {0}" -f $probe.Build)
Write-Host ("  sandbox exe    {0} ({1})" -f $probe.SandboxExe, $(if (Test-Path -LiteralPath $probe.SandboxExe) { 'present' } else { 'missing' }))
Write-Host ("  available      {0}" -f $probe.Available)
if (-not $probe.Available) {
    Write-Host ("  reason         {0}" -f $probe.Reason) -ForegroundColor Yellow
}

$bridge = Test-SandboxDbBridge -ListenPort $Port -TargetPort 55432
Write-Host ("  db bridge      {0} ({1})" -f $bridge.Ready, $bridge.Reason)
Write-Host ''

if ($NoLaunch) {
    exit $(if ($probe.Available) { 0 } else { 1 })
}

if (-not $probe.Available) {
    throw ("Cannot launch a probe: {0}" -f $probe.Reason)
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$outputDirectory = Join-Path $repoRoot ("artifacts\gauntlet\sandbox-probe\{0}" -f $stamp)
$payloadDirectory = Join-Path $outputDirectory 'payload'
$resultsDirectory = Join-Path $outputDirectory 'results'

New-Item -ItemType Directory -Force -Path $payloadDirectory, $resultsDirectory | Out-Null

$guestScript = @'
param([int]$Port = 55433)

$ErrorActionPreference = 'Continue'
$result = [ordered]@{}

try {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing

    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $result.screen = ('{0}x{1}' -f $bounds.Width, $bounds.Height)

    $bitmap = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $result.dpi = $graphics.DpiX
    $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
    $graphics.Dispose()

    $screenPath = 'C:\results\guest-screen.png'
    $bitmap.Save($screenPath, [System.Drawing.Imaging.ImageFormat]::Png)

    $check = New-Object System.Drawing.Bitmap $bitmap
    $first = $check.GetPixel(0, 0)
    $blank = $true
    for ($x = 0; $x -lt $check.Width; $x += [Math]::Max(1, [int]($check.Width / 8))) {
        for ($y = 0; $y -lt $check.Height; $y += [Math]::Max(1, [int]($check.Height / 8))) {
            if ($check.GetPixel($x, $y) -ne $first) { $blank = $false; break }
        }
        if (-not $blank) { break }
    }
    $check.Dispose()
    $bitmap.Dispose()

    $result.capturePath = $screenPath
    $result.captureBlank = $blank

    # A second capture with an application on screen: proves the desktop is composited, not just black.
    $notepad = Start-Process notepad.exe -PassThru
    Start-Sleep -Milliseconds 1500

    $withApp = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
    $graphics = [System.Drawing.Graphics]::FromImage($withApp)
    $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
    $graphics.Dispose()
    $withApp.Save('C:\results\guest-screen-notepad.png', [System.Drawing.Imaging.ImageFormat]::Png)

    $sample = $withApp.GetPixel(0, 0)
    $appBlank = $true
    for ($x = 0; $x -lt $withApp.Width; $x += [Math]::Max(1, [int]($withApp.Width / 8))) {
        for ($y = 0; $y -lt $withApp.Height; $y += [Math]::Max(1, [int]($withApp.Height / 8))) {
            if ($withApp.GetPixel($x, $y) -ne $sample) { $appBlank = $false; break }
        }
        if (-not $appBlank) { break }
    }
    $withApp.Dispose()
    $result.captureWithAppBlank = $appBlank

    $notepad | Stop-Process -Force -ErrorAction SilentlyContinue
}
catch {
    $result.captureError = $_.Exception.Message
}

try {
    $os = Get-CimInstance Win32_OperatingSystem
    $result.os = ('{0} {1} build {2}' -f $os.Caption, $os.Version, $os.BuildNumber)
    $result.user = $env:USERNAME
    $result.dotnetOnPath = [bool](Get-Command dotnet -ErrorAction SilentlyContinue)
    $result.powershell = $PSVersionTable.PSVersion.ToString()

    $route = Get-NetRoute -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue |
        Sort-Object RouteMetric |
        Select-Object -First 1

    if ($route) {
        $result.gateway = $route.NextHop

        $client = New-Object System.Net.Sockets.TcpClient
        try {
            $connect = $client.ConnectAsync($route.NextHop, $Port)
            $result.dbBridgeReachable = $connect.Wait(5000)
        }
        catch {
            $result.dbBridgeReachable = $false
            $result.dbBridgeError = $_.Exception.Message
        }
        finally {
            $client.Dispose()
        }
    }
}
catch {
    $result.environmentError = $_.Exception.Message
}

$result.finishedAt = (Get-Date).ToString('o')

($result | ConvertTo-Json -Depth 4) | Set-Content -LiteralPath 'C:\results\probe.json' -Encoding UTF8
'{"status":"done"}' | Set-Content -LiteralPath 'C:\results\_done.json' -Encoding ASCII
Start-Sleep -Milliseconds 500
& shutdown.exe /s /t 0 /f
'@

$guestPath = Join-Path $payloadDirectory 'probe.ps1'
Set-Content -LiteralPath $guestPath -Value $guestScript -Encoding UTF8

$launchPath = Join-Path $payloadDirectory 'probe.cmd'
Set-Content -LiteralPath $launchPath -Encoding ASCII -Value @"
@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\probe\probe.ps1 -Port $Port
"@

$wsbPath = Join-Path $outputDirectory 'probe.wsb'
$null = New-ShopSandboxConfig `
    -Path $wsbPath `
    -PayloadDirectory $payloadDirectory `
    -ResultsDirectory $resultsDirectory `
    -Command 'cmd.exe /c C:\probe\probe.cmd' `
    -MemoryMb $MemoryMb

$startedAt = Get-Date

try {
    $null = Start-ShopSandbox -ConfigPath $wsbPath -Minimize:(-not $KeepWindow)
    $null = Wait-ShopSandbox -ResultsDirectory $resultsDirectory -TimeoutSeconds $TimeoutSeconds
}
finally {
    $null = Stop-ShopSandbox
}

$elapsed = [int]((Get-Date) - $startedAt).TotalSeconds
$probePath = Join-Path $resultsDirectory 'probe.json'

Write-Host ("Probe finished in {0}s - {1}" -f $elapsed, $probePath) -ForegroundColor Green

if (Test-Path -LiteralPath $probePath) {
    Get-Content -LiteralPath $probePath -Raw
}
else {
    Write-Host 'No probe.json was produced - check the sandbox window and results folder.' -ForegroundColor Yellow
    exit 1
}
