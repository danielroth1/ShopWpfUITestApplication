# UI map

Initial inventory of the shell and every AutomationId a scenario may target. The gauntlet agent's
discovery pass (G4) refreshes this file from a live `Snapshot`; treat code and map as both needing to
agree.

## Discovery status

Verified live on 2026-10-25 (gauntlet iteration 5, run id `20261025-1020-s*`): every id below was
observed in the running app while replaying `ui-tests/scenarios/01..08`. No id was missing or renamed.

Observed details worth knowing when writing scenarios:

- The shell header only exposes `shell.cartButton` and `shell.burgerButton` while signed in; the cart
  button text is `Cart` when empty and `Cart (n)` otherwise.
- `auth.displayName` exists only in register mode (after `auth.toggleMode`); toggle back with the same
  button, whose text changes between `Create a new account` and `I already have an account`.
- The shell window is clamped to the system work area on load (`MainWindow.FitToWorkArea`). Before that
  fix the 1320x860 default window was centered off-screen at 150% scaling and the whole header sat at
  negative Y, unreachable by mouse or UIA.
- Catalog items expose `item.{id}.add` / `item.{id}.save`; the list renders every card, so a scenario
  should search first and then use the name text instead of the id when the seed id is unknown.
- `cart.status` carries the last verb result (`Balance Mat removed.`), `saved.status` likewise.
- Admin rows are keyed by order id (`admin.order.{id}.next` is a `ComboBox`, `admin.order.{id}.apply`
  the commit button), and `admin.status` reports seeding (`Catalog already seeded.`) or adds.
- Checkout keeps defaults for address/holder/expiry/cvc; typing only `checkout.cardNumber` is enough.

## Shell (`MainWindow`)

| AutomationId         | Control | Notes                                    |
| -------------------- | ------- | ---------------------------------------- |
| `app.window`         | window  | title `ShopTest Shop`                    |
| `shell.userLabel`    | text    | signed-in display name, or `Guest`       |
| `shell.cartButton`   | button  | label `Cart (n)`, hidden when signed out |
| `shell.burgerButton` | button  | `☰`, right side of the top bar          |
| `burger.panel`       | panel   | overlay, right aligned                   |
| `burger.account`     | button  | Account settings                         |
| `burger.orders`      | button  | Order history                            |
| `burger.saved`       | button  | Saved items                              |
| `burger.admin`       | button  | Admin panel, only for admins             |
| `burger.catalog`     | button  | Continue shopping                        |
| `burger.signOut`     | button  | Sign out                                 |

## Screens

| Screen             | Key AutomationIds                                                                                                                                                                                                                                                                                             |
| ------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Sign in / Register | `auth.email`, `auth.password`, `auth.displayName`, `auth.submit`, `auth.toggleMode`, `auth.error`, `auth.demoHint`                                                                                                                                                                                            |
| Catalog            | `catalog.search`, `catalog.searchButton`, `catalog.clearButton`, `catalog.openCart`, `catalog.categoryList`, `catalog.items`, `catalog.status`, `item.{id}.add`, `item.{id}.save`                                                                                                                             |
| Cart               | `cart.lines`, `cart.line.{itemId}.quantity`, `cart.line.{itemId}.increase`, `cart.line.{itemId}.decrease`, `cart.line.{itemId}.remove`, `cart.total`, `cart.checkout`, `cart.continue`, `cart.empty`, `cart.status`                                                                                           |
| Checkout           | `checkout.address`, `checkout.cardNumber`, `checkout.holder`, `checkout.expiry`, `checkout.cvc`, `checkout.pay`, `checkout.back`, `checkout.total`, `checkout.error`                                                                                                                                          |
| Confirmation       | `confirmation.orderNumber`, `confirmation.status`, `confirmation.viewOrders`, `confirmation.continue`                                                                                                                                                                                                         |
| Order history      | `orders.list`, `orders.refresh`, `orders.status`, `orders.empty`, `order.{id}.status`, `order.{id}.cancel`, `order.{id}.retry`                                                                                                                                                                                |
| Saved items        | `saved.list`, `saved.empty`, `saved.status`, `saved.{id}.add`, `saved.{id}.remove`                                                                                                                                                                                                                            |
| Account            | `account.displayName`, `account.email`, `account.role`, `account.continue`, `account.signOut`                                                                                                                                                                                                                 |
| Admin              | `admin.seedItems`, `admin.seedUsers`, `admin.refresh`, `admin.newName`, `admin.newDescription`, `admin.newPrice`, `admin.newCategory`, `admin.addItem`, `admin.items`, `admin.item.{id}.remove`, `admin.orders`, `admin.order.{id}.status`, `admin.order.{id}.next`, `admin.order.{id}.apply`, `admin.status` |

## Expected text anchors

- Sign in: `Welcome to ShopTest`
- Catalog status: `N item(s) found.` / `No items match your filters.`
- Cart empty: `Your cart is empty.`
- Declined payment: `The payment was declined. Your cart was kept so you can try again.`
- Confirmation: `Payment approved`
- Order statuses: `Awaiting payment`, `Payment failed`, `Paid`, `Processing`, `Shipped`, `Delivered`, `Cancelled`, `Refunded`

## Known transitions

`PendingPayment -> Paid | PaymentFailed | Cancelled`, `PaymentFailed -> Paid | Cancelled`,
`Paid -> Processing | Cancelled | Refunded`, `Processing -> Shipped | Cancelled | Refunded`,
`Shipped -> Delivered | Refunded`, `Delivered -> Refunded`.
