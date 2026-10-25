# Windows Sandbox plumbing for the UI lane: capability probe, .wsb generation, lifecycle and the
# host-side TCP bridge the guest uses to reach the Podman PostgreSQL container.
# PowerShell 5.1 compatible.

$script:SandboxFirewallRuleName = 'ShopTest sandbox DB bridge'

function Test-IsElevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-WindowsSandboxAvailable {
    $exe = Join-Path $env:SystemRoot 'System32\WindowsSandbox.exe'
    $os = Get-CimInstance Win32_OperatingSystem
    $featureState = -1

    try {
        $feature = Get-CimInstance -ClassName Win32_OptionalFeature -Filter "Name='Containers-DisposableClientVM'" -ErrorAction Stop
        if ($feature) {
            $featureState = [int]$feature.InstallState
        }
    }
    catch {
        $featureState = -1
    }

    $reasons = New-Object System.Collections.ArrayList

    if (-not (Test-Path -LiteralPath $exe)) {
        [void]$reasons.Add("WindowsSandbox.exe is missing from $($env:SystemRoot)\System32")
    }

    if ($featureState -ne 1) {
        $label = switch ($featureState) {
            2 { 'disabled' }
            3 { 'absent' }
            default { 'unknown' }
        }
        [void]$reasons.Add("the Containers-DisposableClientVM feature is $label (enable it with: Enable-WindowsOptionalFeature -Online -FeatureName Containers-DisposableClientVM -All, then reboot)")
    }

    $edition = $os.Caption

    return [pscustomobject]@{
        Available  = ($reasons.Count -eq 0)
        Reason     = ($reasons -join '; ')
        Edition    = $edition
        Build      = $os.BuildNumber
        SandboxExe = $exe
    }
}

function Get-SandboxPortProxy {
    param([int]$ListenPort)

    $lines = & netsh interface portproxy show v4tov4 2>&1 | Out-String

    foreach ($line in ($lines -split "`r?`n")) {
        $match = [regex]::Match($line, '^\s*(\S+)\s+(\d+)\s+(\S+)\s+(\d+)\s*$')
        if ($match.Success -and [int]$match.Groups[2].Value -eq $ListenPort) {
            return [pscustomobject]@{
                ListenAddress  = $match.Groups[1].Value
                ListenPort     = [int]$match.Groups[2].Value
                ConnectAddress = $match.Groups[3].Value
                ConnectPort    = [int]$match.Groups[4].Value
            }
        }
    }

    return $null
}

function Test-SandboxDbBridge {
    param([int]$ListenPort, [int]$TargetPort)

    $proxy = Get-SandboxPortProxy -ListenPort $ListenPort
    if (-not $proxy) {
        return [pscustomobject]@{ Ready = $false; Reason = "no portproxy on $ListenPort" }
    }

    if ($proxy.ConnectPort -ne $TargetPort) {
        return [pscustomobject]@{
            Ready  = $false
            Reason = ("portproxy on {0} points at {1}, expected {2}" -f $ListenPort, $proxy.ConnectPort, $TargetPort)
        }
    }

    $rule = Get-NetFirewallRule -DisplayName $script:SandboxFirewallRuleName -ErrorAction SilentlyContinue
    if (-not $rule) {
        return [pscustomobject]@{ Ready = $false; Reason = 'inbound firewall rule for the bridge is missing' }
    }

    return [pscustomobject]@{ Ready = $true; Reason = ("0.0.0.0:{0} -> 127.0.0.1:{1}" -f $ListenPort, $TargetPort) }
}

# Bridges the sandbox-facing host interfaces to the container port that Podman publishes on loopback.
# Podman on Windows only binds published ports to 127.0.0.1, which a guest VM cannot reach directly.
function Initialize-SandboxDbBridge {
    param(
        [int]$ListenPort = 55433,
        [int]$TargetPort = 55432
    )

    $state = Test-SandboxDbBridge -ListenPort $ListenPort -TargetPort $TargetPort
    if ($state.Ready) {
        return $state.Reason
    }

    if (-not (Test-IsElevated)) {
        $guidance = @"
The sandbox database bridge is not configured and this shell is not elevated.
Run once in an administrator PowerShell:
  netsh interface portproxy add v4tov4 listenaddress=0.0.0.0 listenport=$ListenPort connectaddress=127.0.0.1 connectport=$TargetPort
  New-NetFirewallRule -DisplayName '$($script:SandboxFirewallRuleName)' -Direction Inbound -Action Allow -Protocol TCP -LocalPort $ListenPort -Profile Any
"@
        throw $guidance
    }

    $proxy = Get-SandboxPortProxy -ListenPort $ListenPort
    if ($proxy) {
        & netsh interface portproxy delete v4tov4 listenaddress=$proxy.ListenAddress listenport=$ListenPort | Out-Null
    }

    & netsh interface portproxy add v4tov4 listenaddress=0.0.0.0 listenport=$ListenPort connectaddress=127.0.0.1 connectport=$TargetPort
    if ($LASTEXITCODE -ne 0) {
        throw "netsh interface portproxy add failed with exit code $LASTEXITCODE"
    }

    $rule = Get-NetFirewallRule -DisplayName $script:SandboxFirewallRuleName -ErrorAction SilentlyContinue
    if (-not $rule) {
        New-NetFirewallRule `
            -DisplayName $script:SandboxFirewallRuleName `
            -Direction Inbound `
            -Action Allow `
            -Protocol TCP `
            -LocalPort $ListenPort `
            -Profile Any | Out-Null
    }

    $state = Test-SandboxDbBridge -ListenPort $ListenPort -TargetPort $TargetPort
    if (-not $state.Ready) {
        throw ("sandbox database bridge is still not ready: {0}" -f $state.Reason)
    }

    return $state.Reason
}

function Remove-SandboxDbBridge {
    param([int]$ListenPort = 55433)

    if (-not (Test-IsElevated)) {
        throw 'Removing the sandbox database bridge requires an elevated shell.'
    }

    $proxy = Get-SandboxPortProxy -ListenPort $ListenPort
    if ($proxy) {
        & netsh interface portproxy delete v4tov4 listenaddress=$proxy.ListenAddress listenport=$ListenPort | Out-Null
    }

    Get-NetFirewallRule -DisplayName $script:SandboxFirewallRuleName -ErrorAction SilentlyContinue |
    Remove-NetFirewallRule -ErrorAction SilentlyContinue

    return "removed the bridge on port $ListenPort"
}

function New-ShopSandboxConfig {
    param(
        [string]$Path,
        [string]$PayloadDirectory,
        [string]$ResultsDirectory,
        [string]$Command = 'cmd.exe /c C:\payload\run-in-sandbox.cmd',
        [int]$MemoryMb = 4096
    )

    foreach ($directory in @($PayloadDirectory, $ResultsDirectory)) {
        if (-not (Test-Path -LiteralPath $directory)) {
            throw "Mapped folder does not exist: $directory"
        }
    }

    $payload = [System.Security.SecurityElement]::Escape($PayloadDirectory)
    $results = [System.Security.SecurityElement]::Escape($ResultsDirectory)
    $commandXml = [System.Security.SecurityElement]::Escape($Command)

    $config = @"
<Configuration>
  <VGpu>Disable</VGpu>
  <Networking>Enable</Networking>
  <MemoryInMB>$MemoryMb</MemoryInMB>
  <ClipboardRedirection>Disable</ClipboardRedirection>
  <AudioInput>Disable</AudioInput>
  <VideoInput>Disable</VideoInput>
  <PrinterRedirection>Disable</PrinterRedirection>
  <MappedFolders>
    <MappedFolder>
      <HostFolder>$payload</HostFolder>
      <SandboxFolder>C:\payload</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>$results</HostFolder>
      <SandboxFolder>C:\results</SandboxFolder>
      <ReadOnly>false</ReadOnly>
    </MappedFolder>
  </MappedFolders>
  <LogonCommand>
    <Command>$commandXml</Command>
  </LogonCommand>
</Configuration>
"@

    $parent = Split-Path -Parent $Path
    if ($parent -and -not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }

    # Windows Sandbox requires the UTF-8 BOM for the XML declaration-less config on some builds.
    [System.IO.File]::WriteAllText($Path, $config, (New-Object System.Text.UTF8Encoding($true)))

    return $Path
}

function Get-ShopSandboxProcesses {
    # @() alone is not enough: an empty array collapses to $null on the way out of the function, and
    # StrictMode rejects .Count on $null, so callers must wrap this call in @() themselves.
    return @(Get-Process -Name 'WindowsSandbox', 'WindowsSandboxClient', 'WindowsSandboxRemoteSession' -ErrorAction SilentlyContinue)
}

function Start-ShopSandbox {
    param(
        [Parameter(Mandatory = $true)][string]$ConfigPath,
        [switch]$Minimize
    )

    $running = @(Get-ShopSandboxProcesses)
    if ($running.Count -gt 0) {
        $names = ($running | ForEach-Object { $_.ProcessName }) -join ', '
        throw "A Windows Sandbox instance is already running ($names). Only one instance is supported - close it first."
    }

    $probe = Test-WindowsSandboxAvailable
    if (-not $probe.Available) {
        throw ("Windows Sandbox is not available: {0}" -f $probe.Reason)
    }

    $process = Start-Process -FilePath $probe.SandboxExe -ArgumentList ('"{0}"' -f $ConfigPath) -PassThru

    if ($Minimize) {
        for ($attempt = 0; $attempt -lt 40; $attempt++) {
            Start-Sleep -Milliseconds 250
            $window = Get-Process -Name 'WindowsSandboxClient' -ErrorAction SilentlyContinue |
            Where-Object { $_.MainWindowHandle -ne 0 } |
            Select-Object -First 1

            if ($window) {
                if (-not ('ShopTest.SandboxWindow' -as [type])) {
                    Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern bool ShowWindow(System.IntPtr hWnd, int nCmdShow);' -Name 'SandboxWindow' -Namespace 'ShopTest' | Out-Null
                }

                $null = [ShopTest.SandboxWindow]::ShowWindow($window.MainWindowHandle, 6)
                break
            }
        }
    }

    return $process
}

function Wait-ShopSandbox {
    param(
        [Parameter(Mandatory = $true)][string]$ResultsDirectory,
        [int]$TimeoutSeconds = 900
    )

    $donePath = Join-Path $ResultsDirectory '_done.json'
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $absentSince = $null

    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $donePath) {
            return (Get-Content -LiteralPath $donePath -Raw | ConvertFrom-Json)
        }

        if (@(Get-ShopSandboxProcesses).Count -gt 0) {
            $absentSince = $null
        }
        elseif ($null -eq $absentSince) {
            # The launcher hands off to the client process; a single empty sample is not a crash.
            $absentSince = Get-Date
        }
        elseif (((Get-Date) - $absentSince).TotalSeconds -ge 10) {
            throw "Windows Sandbox exited before the runner wrote _done.json (see $ResultsDirectory)."
        }

        Start-Sleep -Milliseconds 500
    }

    throw "Windows Sandbox did not finish within $TimeoutSeconds seconds (see $ResultsDirectory)."
}

function Stop-ShopSandbox {
    $stopped = 0

    foreach ($process in @(Get-ShopSandboxProcesses)) {
        try {
            Stop-Process -Id $process.Id -Force -ErrorAction Stop
            $stopped++
        }
        catch {
            # Already gone.
        }
    }

    return $stopped
}
