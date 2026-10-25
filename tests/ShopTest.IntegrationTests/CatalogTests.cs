using ShopTest.Core.Services;
using Xunit;

namespace ShopTest.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class CatalogTests(DatabaseFixture fixture)
{
    private readonly CatalogService _catalog = new(fixture.Factory);
    private readonly AdminService _admin = new(fixture.Factory);

    [Fact]
    public async Task Seed_provides_categories_and_items()
    {
        var categories = await _catalog.GetCategoriesAsync();
        var items = await _catalog.SearchAsync(null, null);

        Assert.Contains(categories, c => c.Slug == "books");
        Assert.True(items.Count >= 30, $"expected at least 30 items but found {items.Count}");
    }

    [Fact]
    public async Task Search_matches_name_case_insensitively()
    {
        var results = await _catalog.SearchAsync("headphones", null);

        Assert.Contains(results, i => i.Name == "Aurora Headphones");
    }

    [Fact]
    public async Task Search_filters_by_category()
    {
        var categories = await _catalog.GetCategoriesAsync();
        var books = categories.Single(c => c.Slug == "books");

        var results = await _catalog.SearchAsync(null, books.Id);

        Assert.NotEmpty(results);
        Assert.All(results, i => Assert.Equal("Books", i.Category));
    }

    [Fact]
    public async Task Search_returns_nothing_for_an_unknown_term()
    {
        var results = await _catalog.SearchAsync("zzzz-no-such-item", null);

        Assert.Empty(results);
    }

    [Fact]
    public async Task Removed_items_disappear_from_the_catalog()
    {
        var item = await _admin.AddItemAsync(
            $"Disposable Item {Guid.NewGuid():N}",
            "Created by a test.",
            12.34m,
            (await _catalog.GetCategoriesAsync()).First().Id);

        Assert.Contains(await _catalog.SearchAsync(item.Name, null), i => i.Id == item.Id);

        await _admin.RemoveItemAsync(item.Id);

        Assert.DoesNotContain(await _catalog.SearchAsync(item.Name, null), i => i.Id == item.Id);
    }
}
