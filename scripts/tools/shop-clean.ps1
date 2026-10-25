#Requires -Version 5.1
<#
.SYNOPSIS
    Stops the app and removes bin/obj output for the ShopTest solution.

.DESCRIPTION
    Recovery for stale WPF markup state. An interrupted build (or two builds racing for the
    same obj folder) can leave obj without the generated .baml files, after which the next
    build fails with "error BG1002: File ... cannot be found". Removing obj/bin restores a
    consistent state; the next build regenerates everything.

.PARAMETER KeepAppRunning
    Do not stop a running ShopTest.App process before deleting the output folders.

.EXAMPLE
    .\scripts\tools\shop-clean.ps1
    .\scripts\tools\shop-clean.ps1 -KeepAppRunning
#>
[CmdletBinding()]
param(
    [switch]$KeepAppRunning
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

if (-not $KeepAppRunning) {
    Get-Process -Name 'ShopTest.App' -ErrorAction SilentlyContinue | Stop-Process -Force
}

$targets = @(
    Get-ChildItem -Path (Join-Path $repoRoot 'src'), (Join-Path $repoRoot 'tests') -Directory -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq 'bin' -or $_.Name -eq 'obj' }
)

foreach ($target in $targets) {
    Write-Host ("removing {0}" -f $target.FullName)
    Remove-Item -LiteralPath $target.FullName -Recurse -Force
}

Write-Host ("Removed {0} output folder(s). Next: shop: build (or press F5)." -f $targets.Count)
