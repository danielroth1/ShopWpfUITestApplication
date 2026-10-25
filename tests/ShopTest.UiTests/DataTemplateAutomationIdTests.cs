using System.Windows;
using ShopTest.App.Views;
using ShopTest.UiTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace ShopTest.UiTests;

/// <summary>
/// Covers the ids that only exist once a <c>DataTemplate</c> materializes - the dynamic ones such as
/// <c>cart.line.{id}.remove</c> that a static visual-tree walk can never see.
/// </summary>
/// <remarks>
/// Every seeded collection holds two items with different keys. That is what makes the uniqueness check
/// meaningful: if a template forgot to put the item key in its id, both instances collide and are reported.
/// </remarks>
public sealed class DataTemplateAutomationIdTests(ITestOutputHelper output)
{
    [Fact]
    public void Controls_inside_item_templates_expose_unique_ids_that_include_the_item_key()
    {
        var navigator = new RecordingNavigator();

        (string Name, string ExpectedId, Func<FrameworkElement> Create)[] cases =
        [
            ("CartView/lines", "cart.line.1.increase",
                () => Ui.Show(new CartView(), OfflineUiGraph.Cart(navigator))),
            ("CatalogView/items", "item.1.add",
                () => Ui.Show(new CatalogView(), OfflineUiGraph.Catalog(navigator))),
            ("SavedItemsView/items", "saved.1.add",
                () => Ui.Show(new SavedItemsView(), OfflineUiGraph.Saved(navigator))),
            ("OrderHistoryView/orders", "order.41.status",
                () => Ui.Show(new OrderHistoryView(), OfflineUiGraph.OrderHistory())),
            ("AdminView/items", "admin.item.1.remove",
                () => Ui.Show(new AdminView(), OfflineUiGraph.Admin())),
        ];

        var violations = new List<string>();
        int inspected = 0;

        foreach ((string name, string expectedId, Func<FrameworkElement> create) in cases)
        {
            (ContractReport report, bool realized) = UiHost.Invoke(() =>
            {
                FrameworkElement view = create();
                return (AutomationContract.Check(name, view), Ui.Find(view, expectedId) is not null);
            });

            inspected += report.Authored;
            violations.AddRange(report.Violations);

            if (!realized)
            {
                violations.Add(
                    $"{name}: expected templated element '{expectedId}' was not realized, so this case " +
                    "proves nothing. Either the template no longer renders that control, or the id changed.");
            }

            output.WriteLine(
                $"{name,-26} {report.Authored,3} authored, {report.TemplateParts,3} parts, " +
                $"'{expectedId}' {(realized ? "found" : "MISSING")}");
        }

        output.WriteLine($"{inspected} authored controls across {cases.Length} templated views");

        Assert.True(
            violations.Count == 0,
            "DataTemplate id violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }
}
