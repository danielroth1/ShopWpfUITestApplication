# Gauntlet loop

The gauntlet is a deterministic gate runner. Everything that can be checked mechanically runs from the
script, including the UI scenario replay, which executes inside Windows Sandbox. The agent adds the loop,
the fixes and the interactive triage that a script cannot do.

## Gates

| Gate         | Script                            | What it proves                                                                                                                                   |
| ------------ | --------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------ |
| `preflight`  | `scripts\gates\00-preflight.ps1`  | .NET 10 SDK, `dotnet-ef`, Podman machine, Python 3.15, Windows-MCP installed, MCP endpoint answers an `initialize` request with the bearer token |
| `build`      | `scripts\gates\10-build.ps1`      | `dotnet build ShopTest.slnx -warnaserror` succeeds                                                                                               |
| `database`   | `scripts\gates\20-database.ps1`   | container recreated, `pg_isready`, `dotnet ef database update`                                                                                   |
| `tests`      | `scripts\gates\30-tests.ps1`      | xUnit integration tests against the real container                                                                                               |
| `ui-sandbox` | `scripts\gates\40-ui-sandbox.ps1` | scenarios replayed by `ShopTest.UiRunner` inside Windows Sandbox: app booted, UIA steps, SQL assertions, evidence copied out                     |
| `cleanup`    | `scripts\gates\90-cleanup.ps1`    | app and sandbox processes stopped; the database container stays running so the UI pass can use it (stop it with `-StopContainer`)                |

`ui-sandbox` recreates the database first (`Reset-ShopContainer`), so the scenarios see freshly seeded data
exactly as they do after `-Gate all`. The guest never touches the host desktop: it has its own display and
input stack, the payload is mapped read-only and the results folder is the only writable share. See
[UI tests in Windows Sandbox](sandbox-ui-tests.md).

Windows-MCP remains available for interactive exploration and for triaging a failure that the evidence does
not explain; it is no longer part of the automated UI gate.

## Running

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\gauntlet.ps1 -Gate all -Iteration 1
```

Single gate, keeping the container for UI work:

```powershell
scripts\gauntlet.ps1 -Gate tests -KeepContainer
```

UI gate only:

```powershell
scripts\gauntlet.ps1 -Gate ui-sandbox -Iteration 4
```

Parameters:

- `-Gate all|preflight|build|database|tests|ui-sandbox|cleanup`
- `-Iteration <n>` - selects `artifacts\gauntlet\iter-<n>` and the checkpoint tags
- `-KeepContainer` - reuse the running `shop-pg` container instead of recreating it (database gate)
- `-StopContainer` - stop `shop-pg` during cleanup; by default it stays up for the UI pass
- `-NoUi` - skip the `ui-sandbox` gate inside `-Gate all` (use while Windows Sandbox is not enabled)
- `-NoCommit` - skip the baseline/result commits and tags (explicit opt-out)
- `-Rollback` - discard the working tree and return to the last `gauntlet/iter-*-green` tag
- `-RollbackTo <ref>` - return to a specific tag or commit

Exit code is `0` only when every executed gate passed. The JSON report at
`artifacts\gauntlet\iter-<n>\report.json` is the machine-readable source of truth; the UI gate also records
its scenario evidence under `artifacts\gauntlet\iter-<n>\ui-sandbox\`.

After `-Gate all` the database container is still running with freshly seeded data, so a manual app run or an
interactive MCP pass can start immediately. Pass `-StopContainer` when you are done.

## Report shape

```json
{
  "iteration": 1,
  "startedAt": "2026-10-25T09:12:44.0000000+01:00",
  "finishedAt": "2026-10-25T09:14:02.0000000+01:00",
  "result": "pass",
  "gates": [
    {
      "id": "build",
      "title": "Build solution with warnings as errors",
      "status": "pass",
      "durationMs": 8421,
      "detail": "...",
      "log": "..."
    }
  ]
}
```

## Git checkpoints

`-Gate all` is the loop entry point, and it also manages version control so a bad iteration can be
undone:

1. **Baseline** - if the working tree is dirty it is committed as `gauntlet: iteration <n> baseline`,
   then tagged `gauntlet/iter-<n>-start`.
2. **Result** - after the gates the tree is committed and tagged `gauntlet/iter-<n>-green` (all gates
   passed) or `gauntlet/iter-<n>-failed`.
3. **Report** - `report.json` gains a `git` block:

```json
"git": {
  "enabled": true,
  "baseline": "171ddba8...",
  "baselineTag": "gauntlet/iter-2-start",
  "result": "9c31f0a4...",
  "resultTag": "gauntlet/iter-2-green",
  "resultStatus": "green",
  "committed": true
}
```

Roll back when an iteration made things worse:

```powershell
scripts\gauntlet.ps1 -Rollback                        # last green tag
scripts\gauntlet.ps1 -RollbackTo gauntlet/iter-2-start # discard this iteration only
```

Rollback runs `git reset --hard` plus `git clean -fd`, so uncommitted work (and untracked files) is
discarded. Ignored paths such as `artifacts/`, `bin/` and `obj/` are untouched, which keeps the
verification evidence. Tick a run without commits with `-NoCommit`.

## Loop protocol

See `.github/agents/gauntlet.agent.md`. In short: run the deterministic gates, fix until green, then replay
the UI scenarios; at most 3 iterations per prompt.

## Windows-MCP setup

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\setup-mcp.ps1
```

This installs `windows-mcp` into Python 3.15, generates the auth key in
`%USERPROFILE%\.windows-mcp\config.toml`, registers the `windows-mcp-server` scheduled task, and stores
`WINDOWS_MCP_AUTH_KEY` as a user environment variable.

`.vscode/mcp.json` connects with `Authorization: Bearer ${env:WINDOWS_MCP_AUTH_KEY}`. Restart VS Code after
the first setup so the extension host inherits the variable.

## Troubleshooting

| Symptom                                                   | Fix                                                                                                                                                               |
| --------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `preflight` says MCP did not accept an initialize request | `schtasks /run /tn windows-mcp-server`, then check `%USERPROFILE%\.windows-mcp\server.error.log`                                                                  |
| `401 Unauthorized` from the endpoint                      | The token in `WINDOWS_MCP_AUTH_KEY` no longer matches `config.toml`; rerun `scripts\setup-mcp.ps1`, restart VS Code                                               |
| Podman not reachable                                      | `podman machine start`; the preflight does this once and waits up to 120s                                                                                         |
| Port 55432 already in use                                 | Change `SHOP_DB_PORT` in `.env`, or stop the conflicting container                                                                                                |
| UI automation stalls while you use the mouse              | Windows-MCP holds a desktop lease; run the loop when idle, or set `[control] enabled = false` in `%USERPROFILE%\.windows-mcp\config.toml`                         |
| First `windows-mcp` run is slow                           | Dependency install; subsequent starts are fast                                                                                                                    |
| `comtypes` FutureWarning on Python 3.15                   | Harmless today; if UIA breaks, install Python 3.14 and rerun `setup-mcp.ps1 -Python <path>`                                                                       |
| `ui-sandbox` says Windows Sandbox is not available        | Enable the optional feature and reboot: `Enable-WindowsOptionalFeature -Online -FeatureName Containers-DisposableClientVM -All`; until then run `-Gate all -NoUi` |
| `ui-sandbox` asks for the database bridge                 | The shell is not elevated; run the two `netsh`/`New-NetFirewallRule` commands it prints once. Details in `docs/sandbox-ui-tests.md`                               |
| Every scenario fails at `waitFor` after a click           | Scenario fidelity, not the app: check the UIA dump next to the failing step before changing the app                                                               |

## Local UI sanity tools

When you do not want to run the whole MCP loop, two small scripts check the same accessibility
contract:

```powershell
# while the app is running on the login screen
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\tools\ui-probe.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\tools\ui-login-smoke.ps1 -AddItemToCart
```

`ui-probe.ps1` reports which AutomationIds exist on the current screen; `ui-login-smoke.ps1` signs in,
waits for the catalog and optionally adds the first item to the cart.
