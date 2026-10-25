#Requires -Version 5.1
<#
.SYNOPSIS
    Starts the ShopTest app detached from the terminal.

.DESCRIPTION
    Useful because the shell used by tooling is not always a full cmd/PowerShell profile.
    Waits until the main window is reported, then returns.
#>
[CmdletBinding()]
param(
    [string]$Exe = 'src\ShopTest.App\bin\Debug\net10.0-windows\ShopTest.App.exe',
    [int]$TimeoutSeconds = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not [System.IO.Path]::IsPathRooted($Exe)) {
    $Exe = Join-Path $root $Exe
}

if (-not (Test-Path -LiteralPath $Exe)) {
    throw "Executable not found: $Exe. Run the build gate first."
}

Get-Process -Name 'ShopTest.App' -ErrorAction SilentlyContinue | Stop-Process -Force

$process = Start-Process -FilePath $Exe -WorkingDirectory (Split-Path -Parent $Exe) -PassThru

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while ((Get-Date) -lt $deadline) {
    $process.Refresh()
    if ($process.HasExited) {
        throw "The app exited with code $($process.ExitCode) before showing a window."
    }

    if ($process.MainWindowTitle) {
        Write-Host ("started pid {0} window '{1}'" -f $process.Id, $process.MainWindowTitle)
        exit 0
    }

    Start-Sleep -Milliseconds 500
}

throw "The app did not show a window within $TimeoutSeconds seconds."
