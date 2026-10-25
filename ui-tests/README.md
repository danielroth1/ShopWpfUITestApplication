# UI test scenarios (Windows-MCP)

Each `*.json` file is a declarative, replayable UI test. The gauntlet agent executes them through
Windows-MCP; nothing here runs on its own.

## Execution contract

1. Fresh database (`scripts\gauntlet.ps1 -Gate all` recreates the container and reseeds).
2. Launch the app once per scenario, then kill it in the scenario's `kill` step (or by the agent).
3. Resolve targets in this order: `automationId`, visible `text`, snapshot `label` id.
   `text` matching is a case-insensitive substring match over element names captured in the snapshot.
4. Substitute placeholders before running:
   - `{{runId}}` - unique run id, e.g. `20261025-161500`
   - `{{run.email}}` - `gauntlet+{{runId}}@shop.local`
   - `{{run.name}}` - `Gauntlet {{runId}}`
5. `sqlAssert` steps run with:
   `podman exec shop-pg psql -U shop -d shop -tAc "<query>"`
   and compare the trimmed output with `expect`.
6. Store per-scenario evidence in `artifacts/gauntlet/iter-<n>/ui/<scenario>-<runId>/`:
   `results.json`, `step-XX.png`, `step-XX.snapshot.txt`.

## Scenario order

`00-notepad-smoke.json` only proves the MCP plumbing; skip it once the shop is under test.

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
