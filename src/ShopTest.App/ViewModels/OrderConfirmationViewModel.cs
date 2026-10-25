using CommunityToolkit.Mvvm.Input;
using ShopTest.App.Services;
using ShopTest.Core.Services;

namespace ShopTest.App.ViewModels;

public sealed partial class OrderConfirmationViewModel : ViewModelBase
{
    private readonly IViewNavigator _navigator;

    public OrderConfirmationViewModel(OrderDto order, IViewNavigator navigator)
    {
        Order = order;
        _navigator = navigator;
    }

    public OrderDto Order { get; }

    public override string Title => "Order confirmed";

    public string OrderNumber => $"Order #{Order.Id}";

    public string StatusText => OrderService.StatusLabel(Order.Status);

    public string TotalText => Order.Total.ToString("C");

    public string LineSummary => $"{Order.Lines.Sum(l => l.Quantity)} item(s)";

    public string Address => Order.ShippingAddress;

    [RelayCommand]
    private async Task ViewOrdersAsync() => await _navigator.ShowOrdersAsync();

    [RelayCommand]
    private async Task ContinueShoppingAsync() => await _navigator.ShowCatalogAsync();
}
