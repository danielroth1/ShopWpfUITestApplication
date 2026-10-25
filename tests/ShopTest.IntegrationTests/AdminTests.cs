using ShopTest.Core.Services;
using Xunit;

namespace ShopTest.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class AdminTests(DatabaseFixture fixture)
{
    private readonly AdminService _admin = new(fixture.Factory);

    [Fact]
    public async Task Seeding_items_is_idempotent()
    {
        var added = await _admin.SeedItemsAsync();

        Assert.Equal(0, added);
    }

    [Fact]
    public async Task Seeding_users_is_idempotent()
    {
        var added = await _admin.SeedUsersAsync();

        Assert.Equal(0, added);
    }

    [Fact]
    public async Task Adding_a_duplicate_item_is_rejected()
    {
        var categories = await _admin.GetCategoriesAsync();
        var name = $"Duplicate {Guid.NewGuid():N}";

        await _admin.AddItemAsync(name, "First", 9.99m, categories[0].Id);

        await Assert.ThrowsAsync<ShopException>(() =>
            _admin.AddItemAsync(name, "Second", 19.99m, categories[0].Id));
    }

    [Fact]
    public async Task Adding_an_item_without_a_valid_category_is_rejected()
    {
        await Assert.ThrowsAsync<ShopException>(() =>
            _admin.AddItemAsync($"No category {Guid.NewGuid():N}", "x", 1m, 0));
    }

    [Fact]
    public async Task Removing_an_item_keeps_the_row_but_marks_it_inactive()
    {
        var categories = await _admin.GetCategoriesAsync();
        var item = await _admin.AddItemAsync($"Soft delete {Guid.NewGuid():N}", "x", 5m, categories[0].Id);

        await _admin.RemoveItemAsync(item.Id);

        var all = await _admin.ListItemsAsync();
        var stored = Assert.Single(all, i => i.Id == item.Id);
        Assert.False(stored.IsActive);
    }
}
