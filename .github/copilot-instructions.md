# ShopTest repository instructions

## What this is

A test shop application: a single .NET 10 WPF process (UI + domain + data access) backed by PostgreSQL
running in a throwaway Podman container. There is no separate backend and no HTTP API.

## Layout

- `src/ShopTest.Core` - entities, EF Core `ShopDbContext`, migrations, services, PBKDF2 hashing, seeding.
- `src/ShopTest.App` - WPF shell, MVVM (`CommunityToolkit.Mvvm`), dependency injection via `Microsoft.Extensions.Hosting`.
- `tests/ShopTest.IntegrationTests` - xUnit tests against the real PostgreSQL container.
- `tests/ShopTest.UiTests` - in-process WPF harness (no window, no database).
- `tests/ShopTest.UiRunner` - out-of-process scenario replay (UI Automation) for the host or Windows Sandbox.
- `scripts` - gauntlet runner, per-gate scripts, Podman and MCP helpers.
- `ui-tests` - declarative scenarios (shared by the runner and Windows-MCP) and their JSON schema.
- `docs/sandbox-ui-tests.md` - the sandbox UI lane: payload, bridge, evidence, troubleshooting.
- `.github/agents/gauntlet.agent.md` - the self-verifying loop protocol.

## Conventions

- .NET 10 SDK is installed system-wide (`dotnet` on `PATH`); do not add `DOTNET_ROOT`/`SHOP_DOTNET` workarounds.
- Target framework `net10.0-windows`, nullable enabled, warnings are errors.
- Services take `IDbContextFactory<ShopDbContext>` and create short-lived contexts; never hold a context.
- Business-rule violations throw `ShopException`; the UI shows `ex.Message`.
- Order status changes only through `OrderService.TransitionAsync` (transition whitelist + history rows).
- Payments go through `PaymentService` (offline simulator). Never store full card numbers; only brand and last4.
- Money is `decimal` with precision (10,2); totals come from `OrderTotals.From`.
- Every interactive XAML control needs an `AutomationProperties.AutomationId` (see the instruction file).
- Do not weaken tests or UI scenarios to get a green gauntlet run.

## Verifying UI changes (optional)

Windows-MCP tools (`Snapshot`, `Click`, `Type`, `WaitFor`, `App`) are attached and can drive the running
app through the accessibility tree. They are optional and they hold a desktop lease, so prefer the sandbox
gate or a probe when either can settle the question.

Cheapest check that answers the question wins:

1. `dotnet build` and `scripts\gauntlet.ps1 -Gate tests` - logic, services, persistence.
2. `dotnet test tests\ShopTest.UiTests` - real XAML, bindings and DI wiring; no window, no database, ~1 s.
   This is the cheapest UI check and it covers the AutomationId contract on every screen.
3. `scripts\gauntlet.ps1 -Gate ui-sandbox` - all eight scenarios against the real app and database, run
   inside Windows Sandbox, so the host desktop stays usable (~3 min).
   Same runner on the host (takes over input while it runs):
   `tests\ShopTest.UiRunner\bin\Debug\net10.0-windows\ShopTest.UiRunner.exe --scenarios ui-tests\scenarios --out artifacts\gauntlet\iter-ui-runner --skip 00-notepad-smoke`
4. `scripts\tools\ui-probe.ps1` (app running) - which AutomationIds exist on the current screen.
5. `scripts\tools\ui-login-smoke.ps1 -AddItemToCart` - sign-in plus catalog/cart smoke.
6. Windows-MCP - state a probe cannot see: navigation, enabled/disabled, error text, totals, order status.

MCP is slow (seconds per step) and holds a desktop lease, so prefer a probe or a test when either can
settle it, and don't replay a whole scenario to check one element.

When Windows-MCP is used: launch the app after the database gate, `WaitFor` rather than sleeping, scope
`Snapshot` with `region` and `use_ui_tree: false` when only a picture is needed, resolve targets by
`AutomationId` before visible `text`, and kill the app when done. Name the check you ran and what it showed;
never report a user-visible change as working without one.

## Commands

- Full gate run: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts\gauntlet.ps1 -Gate all -Iteration 1`
- Deterministic gates only (Windows Sandbox not enabled): same command with `-NoUi`
- Sandbox UI gate only: `scripts\gauntlet.ps1 -Gate ui-sandbox -Iteration 1`
- Rebuild the sandbox payload: `scripts\tools\publish-sandbox-payload.ps1 -Force`
- Build only: `scripts\gauntlet.ps1 -Gate build`
- Clean bin/obj after an interrupted build (WPF `BG1002` .baml errors): `scripts\tools\shop-clean.ps1`
- Start/stop the database container (no gauntlet): `scripts\tools\shop-db.ps1 -Action start|stop`
  (VS Code tasks `shop: db start` / `shop: db stop`)
- Recreate the database for a fresh gauntlet run: `scripts\gauntlet.ps1 -Gate database`
- Tests only: `scripts\gauntlet.ps1 -Gate tests -KeepContainer`
- In-process UI tests (no database, no window, no focus steal; ~1 s):
  `dotnet test tests\ShopTest.UiTests`
- Roll back to the last green checkpoint: `scripts\gauntlet.ps1 -Rollback`
- Run the app (migrates and seeds on startup; the database container must be running):
  `dotnet run --project src\ShopTest.App`
- Run an existing build: `src\ShopTest.App\bin\Debug\net10.0-windows\ShopTest.App.exe`
- Debug in VS Code: `F5` with a configuration from `.vscode/launch.json` (`ShopTest.App`,
  `ShopTest.App (attach)`, `ShopTest.IntegrationTests`)

## Seed accounts

- Admin: `admin@shop.local` / `Admin123!`
- Demo client: `client@shop.local` / `Client123!`
- Payment simulator: `4242 4242 4242 4242` succeeds, `4000 0000 0000 0002` declines,
  `4000 0000 0000 9995` reports insufficient funds.
