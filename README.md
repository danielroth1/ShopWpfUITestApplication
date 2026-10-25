# ShopTest

A test shop application: one .NET 10 WPF process (UI + domain + data access) over PostgreSQL in a
throwaway Podman container, verified by a self-checking gauntlet loop and Windows-MCP UI scenarios.

## What works today

- Register / sign in (no email verification), seeded admin and demo client.
- Catalog with category filter and search, add to cart, save for later.
- Cart with quantity updates and totals (8% tax, flat 4.99 shipping under 100).
- Checkout with a simulated payment gateway (success, decline, insufficient funds).
- Order history with status timeline, cancellation and refund on cancel-after-pay.
- Order lifecycle enforced by a transition whitelist with an audit trail.
- Admin panel: seed items/users, add and remove items, advance order status.
- Right-side burger menu: account settings, order history, saved items, admin.

## Prerequisites

- .NET 10 SDK (this machine: `C:\Users\daniel\.dotnet10`, on the user PATH)
- Podman (machine `podman-machine-default`)
- Python 3.14+ with `windows-mcp` for UI automation (`scripts\setup-mcp.ps1`)
- VS Code with Copilot for the agent-driven UI gates

## Quick start

```powershell
# 1. Database + schema + seed (container is recreated)
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\gauntlet.ps1 -Gate database -KeepContainer

# 2. Build and test
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\gauntlet.ps1 -Gate all -Iteration 1

# 3. Run the app (it migrates and seeds on startup)
src\ShopTest.App\bin\Debug\net10.0-windows\ShopTest.App.exe
```

Sign in with `admin@shop.local` / `Admin123!` (admin) or `client@shop.local` / `Client123!` (customer).

## The gauntlet loop

- Runner: `scripts\gauntlet.ps1` (gates + JSON report in `artifacts\gauntlet\iter-<n>\report.json`).
- Agent protocol: `.github/agents/gauntlet.agent.md`, invoked with `/gauntlet`.
- UI scenarios: `ui-tests/scenarios/*.json`, schema in `ui-tests/schema/scenario.schema.json`.
- Details and troubleshooting: `docs/gauntlet.md`.

The loop is: deterministic gates -> fix -> UI scenario replay -> green or stop after 3 iterations.

Every `-Gate all` run is checkpointed in git (`gauntlet/iter-<n>-start`, then
`gauntlet/iter-<n>-green` or `-failed`). To undo a bad iteration:

```powershell
scripts\gauntlet.ps1 -Rollback                         # last green state
scripts\gauntlet.ps1 -RollbackTo gauntlet/iter-1-start  # only the last iteration
```

## Layout

```
src/ShopTest.Core   entities, EF Core context, migrations, services, seeding
src/ShopTest.App    WPF shell, MVVM view models, views
tests/ShopTest.IntegrationTests   xUnit against real PostgreSQL
scripts             gauntlet runner, gates, podman + MCP helpers
ui-tests            declarative Windows-MCP scenarios
docs                gauntlet guide, live UI map
artifacts/gauntlet  per-iteration logs, reports, screenshots (gitignored)
```

## Payment simulator

| Card                  | Outcome            |
| --------------------- | ------------------ |
| `4242 4242 4242 4242` | authorized         |
| `4000 0000 0000 0002` | declined by issuer |
| `4000 0000 0000 9995` | insufficient funds |
| `4000 0000 0000 0069` | expired card       |
| `4000 0000 0000 0119` | processor error    |

Only the brand and last four digits are stored. No card number is persisted.
