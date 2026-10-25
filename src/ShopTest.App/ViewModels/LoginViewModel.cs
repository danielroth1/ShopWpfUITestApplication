using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopTest.App.Services;
using ShopTest.Core.Services;

namespace ShopTest.App.ViewModels;

public sealed partial class LoginViewModel : ViewModelBase
{
    private readonly AuthService _auth;
    private readonly SessionService _session;
    private readonly IViewNavigator _navigator;

    [ObservableProperty]
    private string email = string.Empty;

    [ObservableProperty]
    private string password = string.Empty;

    [ObservableProperty]
    private string displayName = string.Empty;

    [ObservableProperty]
    private bool isRegisterMode;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private bool isBusy;

    public LoginViewModel(AuthService auth, SessionService session, IViewNavigator navigator)
    {
        _auth = auth;
        _session = session;
        _navigator = navigator;
    }

    public override string Title => IsRegisterMode ? "Create account" : "Sign in";

    public string SubmitText => IsRegisterMode ? "Create account" : "Sign in";

    public string ToggleText => IsRegisterMode ? "I already have an account" : "Create a new account";

    partial void OnIsRegisterModeChanged(bool value)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(SubmitText));
        OnPropertyChanged(nameof(ToggleText));
        ErrorMessage = null;
    }

    [RelayCommand]
    private void ToggleMode() => IsRegisterMode = !IsRegisterMode;

    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var user = IsRegisterMode
                ? await _auth.RegisterAsync(Email, Password, DisplayName)
                : await _auth.LoginAsync(Email, Password);

            _session.SignIn(user);
            await _navigator.ShowCatalogAsync();
        }
        catch (ShopException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
