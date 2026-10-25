using ShopTest.Core.Services;
using Xunit;

namespace ShopTest.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class CartTests(DatabaseFixture fixture)
{
    private readonly AuthService _auth = new(fixture.Factory);
    private readonly CartService _cart = new(fixture.Factory);

    [Fact]
    public async Task Adding_the_same_item_twice_increments_quantity()
    {
        var user = await TestData.NewClientAsync(fixture, _auth);
        var item = await TestData.FirstActiveItemAsync(fixture);

        await _cart.AddAsync(user.Id, item.Id);
        var cart = await _cart.AddAsync(user.Id, item.Id);

        var line = Assert.Single(cart.Lines);
        Assert.Equal(2, line.Quantity);
        Assert.Equal(2, cart.ItemCount);
    }

    [Fact]
    public async Task Setting_quantity_to_zero_removes_the_line()
    {
        var user = await TestData.NewClientAsync(fixture, _auth);
        var item = await TestData.FirstActiveItemAsync(fixture);

        await _cart.AddAsync(user.Id, item.Id);
        var cart = await _cart.SetQuantityAsync(user.Id, item.Id, 0);

        Assert.Empty(cart.Lines);
    }

    [Fact]
    public async Task Totals_follow_the_shared_rules()
    {
        var user = await TestData.NewClientAsync(fixture, _auth);
        var items = (await new CatalogService(fixture.Factory).SearchAsync(null, null)).Take(3).ToList();

        foreach (var item in items)
        {
            await _cart.AddAsync(user.Id, item.Id, 2);
        }

        var cart = await _cart.GetAsync(user.Id);
        var expectedSubtotal = items.Sum(i => i.Price * 2);
        var totals = OrderTotals.From(cart.Subtotal);

        Assert.Equal(expectedSubtotal, cart.Subtotal);
        Assert.True(cart.Subtotal >= OrderTotals.FreeShippingThreshold);
        Assert.Equal(0m, totals.Shipping);
        Assert.Equal(cart.Subtotal + totals.Shipping + totals.Tax, totals.Total);
    }

    [Fact]
    public async Task Adding_an_inactive_item_is_rejected()
    {
        var user = await TestData.NewClientAsync(fixture, _auth);
        var admin = new AdminService(fixture.Factory);
        var catalog = new CatalogService(fixture.Factory);

        var item = await admin.AddItemAsync(
            $"Hidden {Guid.NewGuid():N}",
            "Not for sale.",
            5m,
            (await catalog.GetCategoriesAsync()).First().Id);

        await admin.RemoveItemAsync(item.Id);

        await Assert.ThrowsAsync<ShopException>(() => _cart.AddAsync(user.Id, item.Id));
    }
}
