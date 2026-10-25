---
applyTo: "src/ShopTest.App/**/*.xaml"
---

# WPF automation contract

The UI test suite is driven by Windows-MCP through the Windows accessibility tree. Controls that the
agent cannot name are controls the agent cannot test.

## Mandatory identifiers

- Every interactive control (button, text box, combo box, list, check box, expander, menu item) MUST set
  `AutomationProperties.AutomationId`.
- Also set `AutomationProperties.Name` when the visible content is an icon or symbol (for example `☰`).
- Never reuse an AutomationId anywhere in the app, including inside `DataTemplate`s - use an id that
  includes the bound key, for example `item.{0}.add` or `cart.line.{0}.remove`.

## Naming scheme

`area.element.role`, lower camel case:

- `auth.email`, `auth.password`, `auth.submit`
- `catalog.search`, `catalog.categoryList`, `catalog.items`
- `cart.checkout`, `cart.total`
- `checkout.pay`, `checkout.cardNumber`
- `orders.list`, `order.{id}.cancel`
- `burger.account`, `burger.orders`, `burger.saved`, `burger.admin`
- `admin.seedItems`, `admin.newName`, `admin.addItem`

## Layout rules

- Keep the burger menu on the right side of the shell and give each entry a stable id.
- Expose status/error text as a `TextBlock` with an id (`auth.error`, `catalog.status`, ...) so a
  scenario can assert on the message without reading pixels.
- Do not rely on hover, tooltips, or transient toasts for anything a scenario must verify.
- Keep windows single-instance: the loop launches and kills the app repeatedly.

## Accessibility

- Set `AutomationProperties.HelpText` on destructive actions (cancel order, remove item).
- Do not disable focus on controls that scenarios type into.
