using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopTest.App.Services;
using ShopTest.Core.Services;

namespace ShopTest.App.ViewModels;

public sealed partial class CartViewModel : ViewModelBase
{
    private readonly CartService _cart;
    private readonly SessionService _session;
    private readonly IViewNavigator _navigator;

    [ObservableProperty]
    private decimal subtotal;

    [ObservableProperty]
    private decimal shipping;

    [ObservableProperty]
    private decimal tax;

    [ObservableProperty]
    private decimal total;

    [ObservableProperty]
    private string? statusMessage;

    public CartViewModel(CartService cart, SessionService session, IViewNavigator navigator)
    {
        _cart = cart;
        _session = session;
        _navigator = navigator;
    }

    public override string Title => "Your cart";

    public ObservableCollection<CartLineDto> Lines { get; } = new();

    public bool IsEmpty => Lines.Count == 0;

    public override async Task LoadAsync()
    {
        var cart = await _cart.GetAsync(_session.UserId);
        Apply(cart);
    }

    [RelayCommand]
    private async Task IncreaseAsync(CartLineDto? line)
    {
        if (line is null)
        {
            return;
        }

        Apply(await _cart.SetQuantityAsync(_session.UserId, line.ItemId, line.Quantity + 1));
    }

    [RelayCommand]
    private async Task DecreaseAsync(CartLineDto? line)
    {
        if (line is null)
        {
            return;
        }

        Apply(await _cart.SetQuantityAsync(_session.UserId, line.ItemId, line.Quantity - 1));
    }

    [RelayCommand]
    private async Task RemoveAsync(CartLineDto? line)
    {
        if (line is null)
        {
            return;
        }

        Apply(await _cart.RemoveAsync(_session.UserId, line.ItemId));
        StatusMessage = $"{line.Name} removed.";
    }

    [RelayCommand]
    private async Task CheckoutAsync()
    {
        if (Lines.Count == 0)
        {
            StatusMessage = "Add an item before checking out.";
            return;
        }

        await _navigator.ShowCheckoutAsync();
    }

    [RelayCommand]
    private async Task ContinueShoppingAsync() => await _navigator.ShowCatalogAsync();

    private void Apply(CartDto cart)
    {
        Lines.Clear();
        foreach (var line in cart.Lines)
        {
            Lines.Add(line);
        }

        var totals = OrderTotals.From(cart.Subtotal);
        Subtotal = totals.Subtotal;
        Shipping = totals.Shipping;
        Tax = totals.Tax;
        Total = totals.Total;

        OnPropertyChanged(nameof(IsEmpty));
        _navigator.SetCartCount(cart.ItemCount);
    }
}
