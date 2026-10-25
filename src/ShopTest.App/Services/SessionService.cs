using ShopTest.Core.Domain;

namespace ShopTest.App.Services;

/// <summary>Holds the signed-in user for the lifetime of the process.</summary>
public sealed class SessionService
{
    public User? CurrentUser { get; private set; }

    public bool IsAuthenticated => CurrentUser is not null;

    public bool IsAdmin => CurrentUser?.IsAdmin == true;

    public int UserId => CurrentUser?.Id ?? 0;

    public void SignIn(User user) => CurrentUser = user;

    public void SignOut() => CurrentUser = null;
}
