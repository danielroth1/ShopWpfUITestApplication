# Gate 40 - replay the UI scenarios inside Windows Sandbox.
#
# The guest compiles nothing: a cached self-contained payload is mapped read-only, the guest launches the
# app, drives it through UI Automation on its own desktop, writes evidence to a writable mapped folder and
# shuts down. The host never hands over its desktop, so the machine stays usable while the gate runs.

function New-SandboxLaunchScript {
    param(
        [string]$Path,
        [int]$Port,
        [string]$Database,
        [string]$User,
        [string]$Password,
        [string]$RunId
    )

    # The guest resolves the host through its default gateway: the host address differs per boot and
    # Hyper-V switch, and never matches the loopback the container is published on.
    $template = @'
@echo off
setlocal
for /f "usebackq delims=" %%i in (`powershell -NoProfile -Command "(Get-NetRoute -DestinationPrefix '0.0.0.0/0' | Sort-Object RouteMetric | Select-Object -First 1).NextHop"`) do set "SHOP_DB_HOST=%%i"
set "SHOP_DB_CONNECTION="
set "SHOP_DB_PORT=__PORT__"
set "SHOP_DB_NAME=__DB__"
set "SHOP_DB_USER=__USER__"
set "SHOP_DB_PASSWORD=__PASSWORD__"
reg add "HKCU\Software\Microsoft\Avalon.Graphics" /v DisableHWAcceleration /t REG_DWORD /d 1 /f >nul 2>&1
echo guest host: %SHOP_DB_HOST%> C:\results\guest-host.txt
echo db port: %SHOP_DB_PORT%>> C:\results\guest-host.txt
"C:\payload\runner\ShopTest.UiRunner.exe" --scenarios C:\payload\scenarios --out C:\results --app-exe C:\payload\app\ShopTest.App.exe --sandbox --run-id __RUNID__ --skip 00-notepad-smoke> C:\results\runner.log 2>&1
rem Give the guest a moment to flush the evidence files to the mapped folder before the VM dies.
ping -n 4 127.0.0.1 >nul
shutdown /s /t 0 /f
'@

    $content = $template.
    Replace('__PORT__', $Port.ToString()).
    Replace('__DB__', $Database).
    Replace('__USER__', $User).
    Replace('__PASSWORD__', $Password).
    Replace('__RUNID__', $RunId)

    Set-Content -LiteralPath $Path -Value $content -Encoding ASCII
    return $Path
}

function Invoke-GateUiSandbox {
    param([hashtable]$Context)

    $probe = Test-WindowsSandboxAvailable
    if (-not $probe.Available) {
        throw ("Windows Sandbox is not available: {0}" -f $probe.Reason)
    }

    Start-PodmanIfNeeded -Context $Context
    Reset-ShopContainer -Context $Context
    Wait-PostgresReady -Context $Context

    $listenPort = [int](Get-EnvOrDefault 'SHOP_SANDBOX_PORT' '55433')
    $bridge = Initialize-SandboxDbBridge -ListenPort $listenPort -TargetPort ([int]$Context.DbPort)

    $payload = & (Join-Path $Context.Root 'scripts\tools\publish-sandbox-payload.ps1') `
        -OutputRoot (Join-Path $Context.ArtifactsRoot 'payload') `
        -DotnetPath $Context.DotnetPath

    $resultsDirectory = Join-Path $Context.IterationDir 'ui-sandbox'
    if (Test-Path -LiteralPath $resultsDirectory) {
        Remove-Item -LiteralPath $resultsDirectory -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $resultsDirectory | Out-Null

    $runId = 'iter{0}-{1}' -f $Context.Iteration, (Get-Date -Format 'yyyyMMdd-HHmmss')

    $null = New-SandboxLaunchScript `
        -Path (Join-Path $payload.Root 'run-in-sandbox.cmd') `
        -Port $listenPort `
        -Database $Context.DbName `
        -User $Context.DbUser `
        -Password $Context.DbPassword `
        -RunId $runId

    $wsbPath = Join-Path $resultsDirectory 'shop-sandbox.wsb'
    $memoryMb = [int](Get-EnvOrDefault 'SHOP_SANDBOX_MEMORY_MB' '4096')
    $timeoutSeconds = [int](Get-EnvOrDefault 'SHOP_SANDBOX_TIMEOUT_SECONDS' '900')

    $null = New-ShopSandboxConfig `
        -Path $wsbPath `
        -PayloadDirectory $payload.Root `
        -ResultsDirectory $resultsDirectory `
        -MemoryMb $memoryMb

    try {
        $null = Start-ShopSandbox -ConfigPath $wsbPath -Minimize
        $null = Wait-ShopSandbox -ResultsDirectory $resultsDirectory -TimeoutSeconds $timeoutSeconds
    }
    finally {
        $null = Stop-ShopSandbox
    }

    $summaryPath = Join-Path $resultsDirectory 'summary.json'
    if (-not (Test-Path -LiteralPath $summaryPath)) {
        $runnerLog = Join-Path $resultsDirectory 'runner.log'
        $guestOutput = ''

        if (Test-Path -LiteralPath $runnerLog) {
            $lines = @(Get-Content -LiteralPath $runnerLog -ErrorAction SilentlyContinue)
            if ($lines.Count -gt 0) {
                $guestOutput = "`n--- runner.log (last 15 lines) ---`n" + (($lines | Select-Object -Last 15) -join "`n")
            }
            else {
                $guestOutput = "`n(runner.log is empty: the guest runner produced no output)"
            }
        }

        throw ("the sandbox run produced no summary.json - see {0}\runner.log{1}" -f $resultsDirectory, $guestOutput)
    }

    $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json

    if ($summary.result -ne 'pass') {
        $failed = @($summary.scenarios |
            Where-Object { $_.status -ne 'pass' } |
            ForEach-Object { '{0} (step {1})' -f $_.scenario, $_.failedStep })

        throw ("sandbox UI scenarios failed: {0} - evidence in {1}" -f ($failed -join ', '), $resultsDirectory)
    }

    return ("{0} UI scenario(s) green inside Windows Sandbox in {1:0.0}s ({2})" -f
        $summary.scenarioCount,
        ($summary.durationMs / 1000),
        $bridge)
}
