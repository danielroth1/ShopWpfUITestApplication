using Microsoft.EntityFrameworkCore;
using ShopTest.Core.Services;
using Xunit;

namespace ShopTest.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class AuthTests(DatabaseFixture fixture)
{
    private readonly AuthService _auth = new(fixture.Factory);

    [Fact]
    public async Task Register_creates_a_client_account()
    {
        var user = await TestData.NewClientAsync(fixture, _auth);

        Assert.False(user.IsAdmin);
        Assert.True(user.Id > 0);
    }

    [Fact]
    public async Task Register_rejects_a_duplicate_email()
    {
        var email = $"dup-{Guid.NewGuid():N}@shop.local";
        await _auth.RegisterAsync(email, "Password123!", "First");

        var error = await Assert.ThrowsAsync<ShopException>(() =>
            _auth.RegisterAsync(email.ToUpperInvariant(), "Password123!", "Second"));

        Assert.Contains("already exists", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Register_rejects_a_short_password()
    {
        await Assert.ThrowsAsync<ShopException>(() =>
            _auth.RegisterAsync($"short-{Guid.NewGuid():N}@shop.local", "short", "Test"));
    }

    [Fact]
    public async Task Login_with_a_wrong_password_fails()
    {
        var user = await TestData.NewClientAsync(fixture, _auth);

        await Assert.ThrowsAsync<ShopException>(() => _auth.LoginAsync(user.Email, "WrongPassword!"));
    }

    [Fact]
    public async Task Password_is_never_stored_in_plain_text()
    {
        const string password = "Password123!";
        var email = $"plain-{Guid.NewGuid():N}@shop.local";
        await _auth.RegisterAsync(email, password, "Test");

        await using var db = await fixture.Factory.CreateDbContextAsync();
        var stored = await db.Users.SingleAsync(u => u.Email == email);

        Assert.NotEqual(password, stored.PasswordHash);
        Assert.StartsWith("v1.", stored.PasswordHash);
        Assert.DoesNotContain(password, stored.PasswordHash);
    }

    [Fact]
    public async Task Seeded_admin_can_sign_in()
    {
        var admin = await _auth.LoginAsync(Core.Data.DbSeeder.AdminEmail, Core.Data.DbSeeder.AdminPassword);

        Assert.True(admin.IsAdmin);
    }
}
