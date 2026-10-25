using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopTest.App.Services;

namespace ShopTest.App.ViewModels;

public sealed partial class AccountViewModel : ViewModelBase
{
    private readonly SessionService _session;
    private readonly IViewNavigator _navigator;

    public AccountViewModel(SessionService session, IViewNavigator navigator)
    {
        _session = session;
        _navigator = navigator;
    }

    public override string Title => "Account settings";

    public string Email => _session.CurrentUser?.Email ?? string.Empty;

    public string DisplayName => _session.CurrentUser?.DisplayName ?? string.Empty;

    public string Role => _session.IsAdmin ? "Administrator" : "Customer";

    public string MemberSince => _session.CurrentUser?.CreatedAt.ToLocalTime().ToString("dd MMM yyyy") ?? string.Empty;

    [RelayCommand]
    private async Task SignOutAsync()
    {
        _session.SignOut();
        await _navigator.ShowLoginAsync();
    }

    [RelayCommand]
    private async Task BackToCatalogAsync() => await _navigator.ShowCatalogAsync();
}
