# ShopTest

A test shop application: one .NET 10 WPF process (UI + domain + data access) over PostgreSQL in a
throwaway Podman container, verified by an in-process UI test suite, a self-checking gauntlet loop and
full UI scenarios replayed inside Windows Sandbox (Windows-MCP remains available for interactive triage).

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

- .NET 10 SDK installed system-wide: `dotnet --version` must report `10.x` (`global.json` pins
  `10.0.402` with `latestFeature`). No custom `DOTNET_ROOT` or PATH workaround is needed.
- Podman (machine `podman-machine-default`)
- Windows Pro/Enterprise/Education with the Windows Sandbox feature enabled for the `ui-sandbox` gate
  (`Enable-WindowsOptionalFeature -Online -FeatureName Containers-DisposableClientVM -All`, then reboot)
- Python 3.14+ with `windows-mcp` for interactive UI automation (`scripts\setup-mcp.ps1`)
- VS Code with Copilot and the C# extension (C# Dev Kit additionally enables the test debug configuration)

## Quick start

In VS Code, open the Command Palette (`Ctrl+Shift+P`) and run `Tasks: Run Task`:

1. `shop: db start` - starts the Podman machine if needed and starts (or creates) the PostgreSQL container
2. `shop: run app (dotnet run)` - starts the WPF app; it applies migrations and seeds on startup

Or press `F5` with the `ShopTest.App` launch configuration: it starts the container, builds, and
attaches the debugger (`.vscode/launch.json`).

## Running the app

The app applies migrations and seeds demo data on startup, so the PostgreSQL container has to be
running first. The connection defaults to `127.0.0.1:55432` with user/password `shop`/`shop`; override
it with `SHOP_DB_CONNECTION`, or with the `SHOP_DB_HOST` / `SHOP_DB_PORT` / `SHOP_DB_NAME` /
`SHOP_DB_USER` / `SHOP_DB_PASSWORD` variables (a `.env` file in the repository root is also read).

VS Code tasks (`Ctrl+Shift+P` -> `Tasks: Run Task`):

| Task                         | What it does                                                                       |
| ---------------------------- | ---------------------------------------------------------------------------------- |
| `shop: db start`             | starts Podman, then starts or creates the `shop-pg` container on `127.0.0.1:55432` |
| `shop: db stop`              | stops the `shop-pg` container                                                      |
| `shop: build`                | `dotnet build ShopTest.slnx`                                                       |
| `shop: rebuild`              | `dotnet build ShopTest.slnx --no-incremental` (full compile)                       |
| `shop: clean`                | stops the app and deletes every `bin`/`obj` folder                                 |
| `shop: run app (dotnet run)` | builds and starts the app with `dotnet run`                                        |
| `shop: run app`              | starts an existing build                                                           |
| `shop: prepare`              | `shop: db start` followed by `shop: build` (used by the `F5` configuration)        |

Debug configurations in `.vscode/launch.json` (`Ctrl+Shift+D`):

| Configuration               | What it does                                                              |
| --------------------------- | ------------------------------------------------------------------------- |
| `ShopTest.App`              | starts the container, builds, then launches the WPF app with the debugger |
| `ShopTest.App (attach)`     | attaches to an already running `ShopTest.App.exe`                         |
| `ShopTest.IntegrationTests` | debugs the xUnit suite against the container database (must be running)   |

Terminal equivalents:

```powershell
scripts\tools\shop-db.ps1 -Action start   # or -Action stop

dotnet run --project src\ShopTest.App

src\ShopTest.App\bin\Debug\net10.0-windows\ShopTest.App.exe   # existing build
```

### Build fails with `BG1002 ... .baml cannot be found`

That is stale WPF markup state in `obj`, usually left by an interrupted build or by two builds
running at the same time. Run the `shop: clean` task (or `scripts\tools\shop-clean.ps1`), then
`shop: build` or `F5` again; the next build regenerates the markup output. Do not start a second
build (`shop: build`, `shop: run app (dotnet run)`, or `F5`) while one is still running.

Sign in with `admin@shop.local` / `Admin123!` (admin) or `client@shop.local` / `Client123!` (customer).

## The gauntlet loop

- Runner: `scripts\gauntlet.ps1` (gates + JSON report in `artifacts\gauntlet\iter-<n>\report.json`).
- Agent protocol: `.github/agents/gauntlet.agent.md`, invoked with `/gauntlet`.
- UI scenarios: `ui-tests/scenarios/*.json`, schema in `ui-tests/schema/scenario.schema.json`.
- UI executor: `tests\ShopTest.UiRunner` inside Windows Sandbox (`-Gate ui-sandbox`), so the desktop stays usable.
- Details and troubleshooting: `docs/gauntlet.md`, `docs/sandbox-ui-tests.md`.

The loop is: deterministic gates -> fix -> UI scenario replay -> green or stop after 3 iterations.

Every `-Gate all` run is checkpointed in git (`gauntlet/iter-<n>-start`, then
`gauntlet/iter-<n>-green` or `-failed`). To undo a bad iteration:

```powershell
scripts\gauntlet.ps1 -Rollback                         # last green state
scripts\gauntlet.ps1 -RollbackTo gauntlet/iter-1-start  # only the last iteration
```

## In-process UI tests

`tests/ShopTest.UiTests` drives the real XAML from inside the test host on a single STA thread. It creates
no window, takes no desktop lease and touches no database, so it is safe to run while you work:

```powershell
dotnet test tests\ShopTest.UiTests
```

That is the cheapest answer to "did my change break the UI?", so reach for it before the gauntlet or
Windows-MCP.
| Test | What it proves |
| ------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------- |
| `AutomationIdContractTests` | every authored interactive control across 10 screens (the 9 views plus the `MainWindow` shell) has a non-empty, unique `AutomationId` |
| `DataTemplateAutomationIdTests` | ids that only exist once an item template materializes (`cart.line.{id}.remove`, `order.{id}.cancel`, ...) are unique per item |
| `LoginViewInteractionTests` | an automation-peer click reaches the command, updates the view model and re-renders the binding; and that no window is ever shown |
| `CartViewInteractionTests` | a peer click on `cart.continue` reaches the command and navigates - the worked example below, kept compiling |
| `StartupWiringTests` | the real DI container resolves every view model and `MainWindow`, so a missing registration fails here instead of on a user's click |

### How it works

- `UiHost` owns the single WPF `Application` and the STA thread it lives on. UI work goes through
  `UiHost.Invoke`; `UiHost.PumpUntil` pumps the dispatcher, which is required because automation peers
  post their work (`ButtonAutomationPeer.Invoke` dispatches at input priority).
- The real `App` class is constructed so `App.xaml` brushes, styles and converters resolve exactly as at
  runtime. `App.SkipStartupVariable` (`SHOPTEST_SKIP_STARTUP=1`, set by the harness) stops `OnStartup`, so
  there is no host, no migration, no seeding and no window.
- `Ui` is the driver: `Show` (measure + arrange), `Find` / `Require`, `Click`, `SetText`, `Text`,
  `IsEffectivelyVisible`. Everything goes through the same automation peers that UI Automation uses.
- `OfflineUiGraph` builds view models pre-seeded with sample data, and `OfflineDbContextFactory` throws if a
  test reaches the data layer.

### Adding a check

```csharp
[Fact]
public void Continuing_from_the_cart_navigates_to_the_catalog()
{
    var navigator = new RecordingNavigator();
    FrameworkElement view = UiHost.Invoke(() => Ui.Show(new CartView(), OfflineUiGraph.Cart(navigator)));

    UiHost.Invoke(() => Ui.Click(view, "cart.continue"));
    UiHost.PumpUntil(
        () => navigator.Calls.Contains("catalog"),
        "cart.continue should navigate to the catalog");
}
```

### Rules

- **Lay it out before walking it.** A freshly constructed view has no visual tree; `Ui.Show` performs the
  `Measure`/`Arrange` pass that applies templates. A test that walks an unlaid view finds nothing and passes
  for the wrong reason, which is why a screen yielding zero authored controls fails the contract check.
- **No database.** Anything that needs real rows belongs in `ShopTest.IntegrationTests`.
- **Seed at least two items per collection** when exercising a `DataTemplate`. Uniqueness is what catches a
  template whose id forgot the item key; with a single item there is nothing to collide with.
- **Control template parts are not yours.** Controls created by a control's own `ControlTemplate` (a
  `ComboBox`'s drop-down button, an `Expander`'s header toggle) are skipped. Content inside a `DataTemplate`
  is app-authored and is checked.
- **Do not weaken a check to get it green** - same rule as the gauntlet. Fix the XAML or the view model.

### What it deliberately does not cover

- Real input: mouse capture, focus and tab order, hit-testing at real DPI. No HWND is created, so those
  behaviours are out of reach here.
- The startup path itself (migrations, seeding, first-run ordering). `StartupWiringTests` covers the DI
  graph; `ShopTest.IntegrationTests` covers the data.
- Pixels. Offscreen `RenderTargetBitmap` goldens are the obvious next step if visual regression ever matters
  more than the contract.

## Layout

```
src/ShopTest.Core   entities, EF Core context, migrations, services, seeding
src/ShopTest.App    WPF shell, MVVM view models, views
tests/ShopTest.IntegrationTests   xUnit against real PostgreSQL
tests/ShopTest.UiTests            in-process WPF tests: AutomationId contract, bindings, DI wiring
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
