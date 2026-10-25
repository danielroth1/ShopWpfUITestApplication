# UI test scenarios

Each `*.json` file is a declarative, replayable UI test. Two executors replay the same files:

- **`ShopTest.UiRunner`** - the automated one. It runs on the host or inside Windows Sandbox
  (`scripts\gauntlet.ps1 -Gate ui-sandbox`) and needs no desktop lease.
- **Windows-MCP** - the interactive one, for exploration and failure triage. It holds the desktop while it
  runs.

## Execution contract

1. Fresh database (`scripts\gauntlet.ps1 -Gate all` or `-Gate ui-sandbox` recreates the container and
   reseeds). Scenarios share that database; they must not assume exclusive ownership of the seeded client
   account beyond the rows they create.
2. Launch the app once per scenario, then kill it in the scenario's `kill` step (or by the executor).
3. Resolve targets in this order: `automationId`, visible `text`, snapshot `label` id. `text` matching is a
   case-insensitive substring match. A `label`-only target is MCP-specific and is rejected by the runner.
4. Substitute placeholders before running:
   - `{{runId}}` - unique run id, e.g. `20261025-161500`. The runner derives one **per scenario**
     (`<run-id>-s01`), so scenarios in the same iteration cannot collide on email or item name.
   - `{{run.email}}` - `gauntlet+{{runId}}@shop.local`
   - `{{run.name}}` - `Gauntlet {{runId}}`
5. `sqlAssert` steps compare a trimmed scalar with `expect`. Windows-MCP runs them through Podman
   (`podman exec shop-pg psql -U shop -d shop -tAc "<query>"`); `ShopTest.UiRunner` runs them through
   Npgsql using the app's own connection string and formats the value the way `psql -tA` does (`t`/`f`).
   The runner polls until the step timeout, because persistence lags the click.
6. Evidence:
   - sandbox gate: `artifacts/gauntlet/iter-<n>/ui-sandbox/<scenario>-<runId>/` (`results.json`,
     `step-XX-*.png`, failure dumps) plus `summary.json` and `runner.log` for the run.
   - interactive MCP pass: `artifacts/gauntlet/iter-<n>/ui/<scenario>-<runId>/` with the same file names.
7. Screenshots are best-effort. A blank surface is recorded as `skipped` with reason `capture-unavailable`;
   verdicts come from UIA state and SQL, never from pixels.
8. Never "fix" a failing scenario by loosening `expect`; fix the app or the seed.

## Scenario order

`00-notepad-smoke.json` only proves the MCP plumbing; skip it once the shop is under test. The runner skips
it by default in the sandbox gate (`--skip 00-notepad-smoke`).

| File                             | Covers                                              |
| -------------------------------- | --------------------------------------------------- |
| `01-auth-register-login.json`    | registration, no admin escalation, sign out/in      |
| `02-catalog-search-filter.json`  | search, category filter                             |
| `03-cart-add-update-remove.json` | add, increment, remove, cart badge, totals          |
| `04-checkout-success.json`       | paid order, payment row, history rows, cart cleared |
| `05-checkout-decline.json`       | declined card, failed order, cart retained          |
| `06-admin-seed-and-items.json`   | admin gate, idempotent seeding, add item            |
| `07-order-lifecycle.json`        | cancel paid order, refund, audit trail              |
| `08-saved-items.json`            | save from catalog, list, remove                     |

## Notes

- Scenarios share one database per iteration; they must not assume exclusive ownership of the seeded
  client account beyond the rows they create.
- Text targets such as `Add to cart` or `Remove` are used only after a search has narrowed the screen to
  a single card.
- Never "fix" a failing scenario by loosening `expect`; fix the app or the seed.
