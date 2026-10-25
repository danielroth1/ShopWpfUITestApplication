using Microsoft.EntityFrameworkCore;
using ShopTest.Core.Data;
using ShopTest.Core.Domain;
using ShopTest.Core.Security;

namespace ShopTest.Core.Services;

public sealed class AuthService(IDbContextFactory<ShopDbContext> factory)
{
    public async Task<User> RegisterAsync(string email, string password, string displayName, CancellationToken ct = default)
    {
        email = (email ?? string.Empty).Trim().ToLowerInvariant();
        displayName = (displayName ?? string.Empty).Trim();

        if (email.Length < 3 || !email.Contains('@', StringComparison.Ordinal) || email.Contains(' ', StringComparison.Ordinal))
        {
            throw new ShopException("Enter a valid email address.");
        }

        if (password is null || password.Length < 8)
        {
            throw new ShopException("Password must be at least 8 characters long.");
        }

        if (displayName.Length == 0)
        {
            throw new ShopException("Display name is required.");
        }

        await using var db = await factory.CreateDbContextAsync(ct);

        if (await db.Users.AnyAsync(u => u.Email == email, ct))
        {
            throw new ShopException("An account with this email already exists.");
        }

        var user = new User
        {
            Email = email,
            DisplayName = displayName,
            PasswordHash = PasswordHasher.Hash(password),
            IsAdmin = false,
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task<User> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        email = (email ?? string.Empty).Trim().ToLowerInvariant();

        await using var db = await factory.CreateDbContextAsync(ct);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);

        if (user is null || !PasswordHasher.Verify(password ?? string.Empty, user.PasswordHash))
        {
            throw new ShopException("Invalid email or password.");
        }

        return user;
    }

    public async Task<User?> FindByIdAsync(int userId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Users.FindAsync([userId], ct);
    }
}
