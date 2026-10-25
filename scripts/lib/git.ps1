# Git checkpoints for the gauntlet loop. PowerShell 5.1 compatible.
#
# Every `-Gate all` run creates:
#   * a baseline commit/tag when the working tree has uncommitted work,
#   * a result commit plus tag (`green` or `failed`) when the run ends,
# so the agent can always jump back to the state before an iteration.

function Invoke-Git {
    param(
        [hashtable]$Context,
        [string[]]$Arguments,
        [switch]$AllowFailure
    )

    $output = & $Context.GitPath -C $Context.Root @Arguments 2>&1
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0 -and -not $AllowFailure) {
        throw ("git {0} failed ({1}): {2}" -f ($Arguments -join ' '), $exitCode, (($output | Out-String).Trim()))
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Output   = (($output | Out-String).Trim())
    }
}

function Test-GitRepository {
    param([hashtable]$Context)

    return (Test-Path -LiteralPath (Join-Path $Context.Root '.git'))
}

function Initialize-GitRepository {
    param([hashtable]$Context)

    if (Test-GitRepository -Context $Context) {
        return
    }

    Invoke-Git -Context $Context -Arguments @('init') | Out-Null

    $branch = Invoke-Git -Context $Context -Arguments @('symbolic-ref', '--short', 'HEAD') -AllowFailure
    if ($branch.ExitCode -ne 0 -or $branch.Output -ne 'main') {
        Invoke-Git -Context $Context -Arguments @('symbolic-ref', 'HEAD', 'refs/heads/main') | Out-Null
    }

    Write-Host '    initialized git repository on branch main'
}

function Ensure-GitIdentity {
    param([hashtable]$Context)

    $name = Invoke-Git -Context $Context -Arguments @('config', 'user.name') -AllowFailure
    if ([string]::IsNullOrWhiteSpace($name.Output)) {
        Invoke-Git -Context $Context -Arguments @('config', 'user.name', 'ShopTest Gauntlet') | Out-Null
        Invoke-Git -Context $Context -Arguments @('config', 'user.email', 'gauntlet@shop.local') | Out-Null
    }
}

function Get-GitHead {
    param([hashtable]$Context)

    $result = Invoke-Git -Context $Context -Arguments @('rev-parse', 'HEAD') -AllowFailure
    if ($result.ExitCode -ne 0) {
        return $null
    }

    return $result.Output
}

function Test-GitDirty {
    param([hashtable]$Context)

    $status = Invoke-Git -Context $Context -Arguments @('status', '--porcelain')
    return ($status.Output.Length -gt 0)
}

function New-GitCheckpoint {
    param(
        [hashtable]$Context,
        [string]$Message
    )

    Initialize-GitRepository -Context $Context
    Ensure-GitIdentity -Context $Context

    $committed = $false

    if (Test-GitDirty -Context $Context) {
        Invoke-Git -Context $Context -Arguments @('add', '-A') | Out-Null
        Invoke-Git -Context $Context -Arguments @('commit', '-m', $Message) | Out-Null
        $committed = $true
    }

    $sha = Get-GitHead -Context $Context

    return [pscustomobject]@{
        Sha       = $sha
        Committed = $committed
        Message   = $Message
    }
}

function Set-GitCheckpointTag {
    param(
        [hashtable]$Context,
        [string]$Tag,
        [string]$Sha,
        [string]$Message
    )

    if ([string]::IsNullOrWhiteSpace($Sha)) {
        return
    }

    Invoke-Git -Context $Context -Arguments @('tag', '-f', '-a', $Tag, '-m', $Message, $Sha) | Out-Null
}

function Get-LatestGreenTag {
    param([hashtable]$Context)

    $tags = Invoke-Git -Context $Context -Arguments @('tag', '--list', 'gauntlet/iter-*-green', '--sort=-creatordate') -AllowFailure
    if ($tags.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($tags.Output)) {
        return $null
    }

    return (($tags.Output -split "`r?`n") | Where-Object { $_.Trim().Length -gt 0 } | Select-Object -First 1).Trim()
}

function Resolve-GitRef {
    param(
        [hashtable]$Context,
        [string]$Ref
    )

    $resolved = Invoke-Git -Context $Context -Arguments @('rev-parse', '--verify', "$Ref^{commit}") -AllowFailure
    if ($resolved.ExitCode -ne 0) {
        throw "Unknown git ref '$Ref'."
    }

    return $resolved.Output
}

function Restore-GitCheckpoint {
    param(
        [hashtable]$Context,
        [string]$Ref
    )

    $sha = Resolve-GitRef -Context $Context -Ref $Ref

    Invoke-Git -Context $Context -Arguments @('reset', '--hard', $sha) | Out-Null
    Invoke-Git -Context $Context -Arguments @('clean', '-fd') | Out-Null

    return $sha
}

function Get-GitShortSha {
    param([string]$Sha)

    if ([string]::IsNullOrWhiteSpace($Sha)) {
        return '(none)'
    }

    if ($Sha.Length -le 8) {
        return $Sha
    }

    return $Sha.Substring(0, 8)
}
