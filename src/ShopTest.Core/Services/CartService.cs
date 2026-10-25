using Microsoft.EntityFrameworkCore;
using ShopTest.Core.Data;
using ShopTest.Core.Domain;

namespace ShopTest.Core.Services;

public sealed class CartService(IDbContextFactory<ShopDbContext> factory)
{
    public async Task<CartDto> GetAsync(int userId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await LoadAsync(db, userId, ct);
    }

    public async Task<CartDto> AddAsync(int userId, int itemId, int quantity = 1, CancellationToken ct = default)
    {
        if (quantity < 1)
        {
            throw new ShopException("Quantity must be at least 1.");
        }

        await using var db = await factory.CreateDbContextAsync(ct);

        var item = await db.Items.SingleOrDefaultAsync(i => i.Id == itemId && i.IsActive, ct)
            ?? throw new ShopException("This item is no longer available.");

        var existing = await db.CartItems.SingleOrDefaultAsync(c => c.UserId == userId && c.ItemId == itemId, ct);
        if (existing is null)
        {
            db.CartItems.Add(new CartItem { UserId = userId, ItemId = itemId, Quantity = quantity });
        }
        else
        {
            existing.Quantity += quantity;
        }

        await db.SaveChangesAsync(ct);
        _ = item;
        return await LoadAsync(db, userId, ct);
    }

    public async Task<CartDto> SetQuantityAsync(int userId, int itemId, int quantity, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var existing = await db.CartItems.SingleOrDefaultAsync(c => c.UserId == userId && c.ItemId == itemId, ct);
        if (existing is null)
        {
            return await LoadAsync(db, userId, ct);
        }

        if (quantity <= 0)
        {
            db.CartItems.Remove(existing);
        }
        else
        {
            existing.Quantity = quantity;
        }

        await db.SaveChangesAsync(ct);
        return await LoadAsync(db, userId, ct);
    }

    public async Task<CartDto> RemoveAsync(int userId, int itemId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var existing = await db.CartItems.SingleOrDefaultAsync(c => c.UserId == userId && c.ItemId == itemId, ct);
        if (existing is not null)
        {
            db.CartItems.Remove(existing);
            await db.SaveChangesAsync(ct);
        }

        return await LoadAsync(db, userId, ct);
    }

    public async Task ClearAsync(int userId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var items = await db.CartItems.Where(c => c.UserId == userId).ToListAsync(ct);
        db.CartItems.RemoveRange(items);
        await db.SaveChangesAsync(ct);
    }

    private static async Task<CartDto> LoadAsync(ShopDbContext db, int userId, CancellationToken ct)
    {
        var lines = await db.CartItems
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.Item!.Name)
            .Select(c => new CartLineDto(c.ItemId, c.Item!.Name, c.Item.Price, c.Quantity))
            .ToListAsync(ct);

        return new CartDto(lines);
    }
}
