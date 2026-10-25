# ShopTest repository instructions

## What this is

A test shop application: a single .NET 10 WPF process (UI + domain + data access) backed by PostgreSQL
running in a throwaway Podman container. There is no separate backend and no HTTP API.

## Layout

- `src/ShopTest.Core` - entities, EF Core `ShopDbContext`, migrations, services, PBKDF2 hashing, seeding.
- `src/ShopTest.App` - WPF shell, MVVM (`CommunityToolkit.Mvvm`), dependency injection via `Microsoft.Extensions.Hosting`.
- `tests/ShopTest.IntegrationTests` - xUnit tests against the real PostgreSQL container.
- `scripts` - gauntlet runner, per-gate scripts, Podman and MCP helpers.
- `ui-tests` - declarative Windows-MCP scenarios and their JSON schema.
- `.github/agents/gauntlet.agent.md` - the self-verifying loop protocol.

## Conventions

- Target framework `net10.0-windows`, nullable enabled, warnings are errors.
- Services take `IDbContextFactory<ShopDbContext>` and create short-lived contexts; never hold a context.
- Business-rule violations throw `ShopException`; the UI shows `ex.Message`.
- Order status changes only through `OrderService.TransitionAsync` (transition whitelist + history rows).
- Payments go through `PaymentService` (offline simulator). Never store full card numbers; only brand and last4.
- Money is `decimal` with precision (10,2); totals come from `OrderTotals.From`.
- Every interactive XAML control needs an `AutomationProperties.AutomationId` (see the instruction file).
- Do not weaken tests or UI scenarios to get a green gauntlet run.

## Commands

- Full gate run: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts\gauntlet.ps1 -Gate all -Iteration 1`
- Build only: `scripts\gauntlet.ps1 -Gate build`
- Database only: `scripts\gauntlet.ps1 -Gate database -KeepContainer`
- Tests only: `scripts\gauntlet.ps1 -Gate tests -KeepContainer`
- Roll back to the last green checkpoint: `scripts\gauntlet.ps1 -Rollback`
- Run the app: `src\ShopTest.App\bin\Debug\net10.0-windows\ShopTest.App.exe`

## Seed accounts

- Admin: `admin@shop.local` / `Admin123!`
- Demo client: `client@shop.local` / `Client123!`
- Payment simulator: `4242 4242 4242 4242` succeeds, `4000 0000 0000 0002` declines,
  `4000 0000 0000 9995` reports insufficient funds.
