using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ShopTest.App.Services;
using ShopTest.Core.Services;

namespace ShopTest.App.ViewModels;

public sealed partial class ShellViewModel : ViewModelBase, IViewNavigator
{
    private readonly IServiceProvider _services;
    private readonly CartService _cart;

    [ObservableProperty]
    private ViewModelBase? currentViewModel;

    [ObservableProperty]
    private bool isBurgerOpen;

    [ObservableProperty]
    private int cartCount;

    [ObservableProperty]
    private string userLabel = "Guest";

    [ObservableProperty]
    private bool isLoggedIn;

    [ObservableProperty]
    private bool isAdmin;

    public ShellViewModel(IServiceProvider services, SessionService session, CartService cart)
    {
        _services = services;
        _cart = cart;
        Session = session;
    }

    public SessionService Session { get; }

    public override string Title => "ShopTest";

    public string CartButtonText => CartCount > 0 ? $"Cart ({CartCount})" : "Cart";

    partial void OnCartCountChanged(int value) => OnPropertyChanged(nameof(CartButtonText));

    public async Task ShowLoginAsync()
    {
        var vm = _services.GetRequiredService<LoginViewModel>();
        await ActivateAsync(vm);
    }

    public async Task ShowCatalogAsync()
    {
        var vm = _services.GetRequiredService<CatalogViewModel>();
        await ActivateAsync(vm);
    }

    public async Task ShowCartAsync()
    {
        var vm = _services.GetRequiredService<CartViewModel>();
        await ActivateAsync(vm);
    }

    public async Task ShowCheckoutAsync()
    {
        var vm = _services.GetRequiredService<CheckoutViewModel>();
        await ActivateAsync(vm);
    }

    public async Task ShowOrdersAsync()
    {
        var vm = _services.GetRequiredService<OrderHistoryViewModel>();
        await ActivateAsync(vm);
    }

    public async Task ShowSavedAsync()
    {
        var vm = _services.GetRequiredService<SavedItemsViewModel>();
        await ActivateAsync(vm);
    }

    public async Task ShowAccountAsync()
    {
        var vm = _services.GetRequiredService<AccountViewModel>();
        await ActivateAsync(vm);
    }

    public async Task ShowAdminAsync()
    {
        var vm = _services.GetRequiredService<AdminViewModel>();
        await ActivateAsync(vm);
    }

    public async Task ShowConfirmationAsync(OrderDto order)
    {
        var vm = new OrderConfirmationViewModel(order, this);
        await ActivateAsync(vm);
    }

    public void SetCartCount(int count) => CartCount = count;

    [RelayCommand]
    private async Task OpenCatalogAsync() => await ShowCatalogAsync();

    [RelayCommand]
    private async Task OpenCartAsync() => await ShowCartAsync();

    [RelayCommand]
    private void ToggleBurger() => IsBurgerOpen = !IsBurgerOpen;

    [RelayCommand]
    private async Task OpenAccountAsync() => await ShowAccountAsync();

    [RelayCommand]
    private async Task OpenOrdersAsync() => await ShowOrdersAsync();

    [RelayCommand]
    private async Task OpenSavedAsync() => await ShowSavedAsync();

    [RelayCommand]
    private async Task OpenAdminAsync() => await ShowAdminAsync();

    [RelayCommand]
    private async Task SignOutAsync()
    {
        Session.SignOut();
        CartCount = 0;
        await ShowLoginAsync();
    }

    private async Task ActivateAsync(ViewModelBase viewModel)
    {
        IsBurgerOpen = false;
        CurrentViewModel = viewModel;
        await viewModel.LoadAsync();
        await RefreshHeaderAsync();
    }

    private async Task RefreshHeaderAsync()
    {
        IsLoggedIn = Session.IsAuthenticated;
        IsAdmin = Session.IsAdmin;
        UserLabel = Session.CurrentUser?.DisplayName ?? "Guest";

        if (!IsLoggedIn)
        {
            CartCount = 0;
            return;
        }

        try
        {
            var cart = await _cart.GetAsync(Session.UserId);
            CartCount = cart.ItemCount;
        }
        catch (ShopException)
        {
            CartCount = 0;
        }
    }
}
