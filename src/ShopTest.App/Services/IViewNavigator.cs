using ShopTest.Core.Services;

namespace ShopTest.App.Services;

/// <summary>Navigation surface used by view models. Implemented by the shell.</summary>
public interface IViewNavigator
{
    Task ShowLoginAsync();

    Task ShowCatalogAsync();

    Task ShowCartAsync();

    Task ShowCheckoutAsync();

    Task ShowOrdersAsync();

    Task ShowSavedAsync();

    Task ShowAccountAsync();

    Task ShowAdminAsync();

    Task ShowConfirmationAsync(OrderDto order);

    void SetCartCount(int count);
}
