# Gate 90 - stop the app and (optionally) the database container.

function Invoke-GateCleanup {
    param([hashtable]$Context)

    $stopped = 0
    Get-Process -Name 'ShopTest.App' -ErrorAction SilentlyContinue | ForEach-Object {
        Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
        $stopped++
    }

    if ($Context.StopContainer) {
        Stop-ShopContainer -Context $Context
    }

    return ("stopped {0} app process(es); database container stopped: {1}" -f $stopped, $Context.StopContainer)
}
