# Gauntlet loop

The gauntlet is a deterministic gate runner plus an agent-driven UI verification pass. The script owns
everything that can be checked mechanically; the agent owns everything that needs eyes on the UI.

## Gates

| Gate | Script | What it proves |
| --- | --- | --- |
| `preflight` | `scripts\gates\00-preflight.ps1` | .NET 10 SDK, `dotnet-ef`, Podman machine, Python 3.15, Windows-MCP installed, MCP endpoint answers an `initialize` request with the bearer token |
| `build` | `scripts\gates\10-build.ps1` | `dotnet build ShopTest.slnx -warnaserror` succeeds |
| `database` | `scripts\gates\20-database.ps1` | container recreated, `pg_isready`, `dotnet ef database update` |
| `tests` | `scripts\gates\30-tests.ps1` | xUnit integration tests against the real container |
| `cleanup` | `scripts\gates\90-cleanup.ps1` | app processes stopped; the database container stays running so the UI pass can use it (stop it with `-StopContainer`) |

UI gates (G4 discovery, G5 scenario replay) are executed by the agent using Windows-MCP and write their
evidence under `artifacts/gauntlet/iter-<n>/ui/`.

## Running

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\gauntlet.ps1 -Gate all -Iteration 1
```

Single gate, keeping the container for UI work:

```powershell
scripts\gauntlet.ps1 -Gate tests -KeepContainer
```

Parameters:

- `-Gate all|preflight|build|database|tests|cleanup`
- `-Iteration <n>` - selects `artifacts\gauntlet\iter-<n>`
- `-KeepContainer` - reuse the running `shop-pg` container instead of recreating it (database gate)
- `-StopContainer` - stop `shop-pg` during cleanup; by default it stays up for the UI pass

Exit code is `0` only when every executed gate passed. The JSON report at
`artifacts\gauntlet\iter-<n>\report.json` is the machine-readable source of truth.

After `-Gate all` the database container is still running with freshly seeded data, so the agent can
launch the app and replay the UI scenarios immediately. Pass `-StopContainer` when you are done.

## Report shape

```json
{
  "iteration": 1,
  "startedAt": "2026-10-25T09:12:44.0000000+01:00",
  "finishedAt": "2026-10-25T09:14:02.0000000+01:00",
  "result": "pass",
  "gates": [
    { "id": "build", "title": "Build solution with warnings as errors", "status": "pass", "durationMs": 8421, "detail": "...", "log": "..." }
  ]
}
```

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

| Symptom | Fix |
| --- | --- |
| `preflight` says MCP did not accept an initialize request | `schtasks /run /tn windows-mcp-server`, then check `%USERPROFILE%\.windows-mcp\server.error.log` |
| `401 Unauthorized` from the endpoint | The token in `WINDOWS_MCP_AUTH_KEY` no longer matches `config.toml`; rerun `scripts\setup-mcp.ps1`, restart VS Code |
| Podman not reachable | `podman machine start`; the preflight does this once and waits up to 120s |
| Port 55432 already in use | Change `SHOP_DB_PORT` in `.env`, or stop the conflicting container |
| UI automation stalls while you use the mouse | Windows-MCP holds a desktop lease; run the loop when idle, or set `[control] enabled = false` in `%USERPROFILE%\.windows-mcp\config.toml` |
| First `windows-mcp` run is slow | Dependency install; subsequent starts are fast |
| `comtypes` FutureWarning on Python 3.15 | Harmless today; if UIA breaks, install Python 3.14 and rerun `setup-mcp.ps1 -Python <path>` |

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

