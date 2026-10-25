using Microsoft.EntityFrameworkCore;
using ShopTest.Core.Data;
using ShopTest.Core.Domain;

namespace ShopTest.Core.Services;

public sealed class AdminService(IDbContextFactory<ShopDbContext> factory)
{
    public async Task<int> SeedItemsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await DbSeeder.SeedCatalogAsync(db, ct);
    }

    public async Task<int> SeedUsersAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await DbSeeder.SeedDemoUsersAsync(db, ct);
    }

    public async Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Categories
            .OrderBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Slug))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ItemDto>> ListItemsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Items
            .OrderBy(i => i.Name)
            .Select(i => new ItemDto(
                i.Id,
                i.Name,
                i.Description,
                i.Price,
                i.Category!.Name,
                i.CategoryId,
                i.IsActive))
            .ToListAsync(ct);
    }

    public async Task<ItemDto> AddItemAsync(string name, string description, decimal price, int categoryId, CancellationToken ct = default)
    {
        name = (name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new ShopException("Item name is required.");
        }

        if (price <= 0m)
        {
            throw new ShopException("Price must be greater than zero.");
        }

        await using var db = await factory.CreateDbContextAsync(ct);

        var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == categoryId, ct)
            ?? throw new ShopException("Select a category.");

        if (await db.Items.AnyAsync(i => i.Name == name, ct))
        {
            throw new ShopException("An item with this name already exists.");
        }

        var item = new Item
        {
            Name = name,
            Description = (description ?? string.Empty).Trim(),
            Price = decimal.Round(price, 2),
            CategoryId = category.Id,
        };

        db.Items.Add(item);
        await db.SaveChangesAsync(ct);

        return new ItemDto(item.Id, item.Name, item.Description, item.Price, category.Name, category.Id, item.IsActive);
    }

    public async Task RemoveItemAsync(int itemId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var item = await db.Items.SingleOrDefaultAsync(i => i.Id == itemId, ct)
            ?? throw new ShopException("Item not found.");

        item.IsActive = false;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<OrderDto>> ListOrdersAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var orders = await db.Orders
            .Include(o => o.Lines)
            .Include(o => o.StatusHistory)
            .OrderByDescending(o => o.PlacedAt)
            .ToListAsync(ct);

        return orders.Select(o => new OrderDto(
            o.Id,
            o.Status,
            o.PaymentStatus,
            o.PlacedAt,
            o.Subtotal,
            o.Shipping,
            o.Tax,
            o.Total,
            o.ShippingAddress,
            o.Lines.Select(l => new OrderLineDto(l.NameSnapshot, l.UnitPrice, l.Quantity)).ToList(),
            o.StatusHistory.OrderBy(h => h.ChangedAt).Select(h => new OrderStatusChangeDto(h.FromStatus, h.ToStatus, h.ChangedAt, h.ChangedBy, h.Reason)).ToList()))
            .ToList();
    }
}
