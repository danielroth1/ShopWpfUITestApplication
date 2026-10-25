using Microsoft.EntityFrameworkCore;
using ShopTest.Core.Data;
using ShopTest.Core.Domain;

namespace ShopTest.Core.Services;

public sealed class SavedItemsService(IDbContextFactory<ShopDbContext> factory)
{
    public async Task<IReadOnlyList<ItemDto>> ListAsync(int userId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        return await db.SavedItems
            .Where(s => s.UserId == userId)
            .OrderBy(s => s.Item!.Name)
            .Select(s => new ItemDto(
                s.ItemId,
                s.Item!.Name,
                s.Item.Description,
                s.Item.Price,
                s.Item.Category!.Name,
                s.Item.CategoryId,
                s.Item.IsActive))
            .ToListAsync(ct);
    }

    /// <summary>Adds the item when absent, removes it when present. Returns true when saved.</summary>
    public async Task<bool> ToggleAsync(int userId, int itemId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var existing = await db.SavedItems.SingleOrDefaultAsync(s => s.UserId == userId && s.ItemId == itemId, ct);
        if (existing is not null)
        {
            db.SavedItems.Remove(existing);
            await db.SaveChangesAsync(ct);
            return false;
        }

        if (!await db.Items.AnyAsync(i => i.Id == itemId && i.IsActive, ct))
        {
            throw new ShopException("This item is no longer available.");
        }

        db.SavedItems.Add(new SavedItem { UserId = userId, ItemId = itemId });
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> IsSavedAsync(int userId, int itemId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.SavedItems.AnyAsync(s => s.UserId == userId && s.ItemId == itemId, ct);
    }
}
