using Microsoft.EntityFrameworkCore;
using ShopTest.Core.Data;

namespace ShopTest.Core.Services;

public sealed class CatalogService(IDbContextFactory<ShopDbContext> factory)
{
    public async Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Categories
            .OrderBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Slug))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ItemDto>> SearchAsync(string? query, int? categoryId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var items = db.Items.Where(i => i.IsActive);

        if (categoryId is > 0)
        {
            items = items.Where(i => i.CategoryId == categoryId);
        }

        var term = (query ?? string.Empty).Trim();
        if (term.Length > 0)
        {
            var pattern = $"%{term}%";
            items = items.Where(i =>
                EF.Functions.ILike(i.Name, pattern) ||
                EF.Functions.ILike(i.Description, pattern));
        }

        return await items
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
}
