using System.Windows;
using ShopTest.App.Views;
using ShopTest.UiTests.Infrastructure;
using Xunit;

namespace ShopTest.UiTests;

/// <summary>
/// The worked example from the README ("In-process UI tests / Adding a check"), kept here so the documented
/// pattern stays compile- and pass-checked instead of aspirational.
/// </summary>
public sealed class CartViewInteractionTests
{
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
}
