using ShopTest.App.Services;
using ShopTest.Core.Services;

namespace ShopTest.UiTests.Infrastructure;

/// <summary>Records navigation requests instead of moving a real shell around.</summary>
internal sealed class RecordingNavigator : IViewNavigator
{
    public List<string> Calls { get; } = [];

    public Task ShowLoginAsync() => Record("login");

    public Task ShowCatalogAsync() => Record("catalog");

    public Task ShowCartAsync() => Record("cart");

    public Task ShowCheckoutAsync() => Record("checkout");

    public Task ShowOrdersAsync() => Record("orders");

    public Task ShowSavedAsync() => Record("saved");

    public Task ShowAccountAsync() => Record("account");

    public Task ShowAdminAsync() => Record("admin");

    public Task ShowConfirmationAsync(OrderDto order) => Record("confirmation");

    public void SetCartCount(int count) => Calls.Add($"cart:{count}");

    private Task Record(string call)
    {
        Calls.Add(call);
        return Task.CompletedTask;
    }
}
