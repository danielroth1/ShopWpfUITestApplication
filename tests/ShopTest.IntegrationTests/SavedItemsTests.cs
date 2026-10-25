using ShopTest.Core.Services;
using Xunit;

namespace ShopTest.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class SavedItemsTests(DatabaseFixture fixture)
{
    private readonly AuthService _auth = new(fixture.Factory);
    private readonly SavedItemsService _saved = new(fixture.Factory);

    [Fact]
    public async Task Toggle_adds_then_removes_the_item()
    {
        var user = await TestData.NewClientAsync(fixture, _auth);
        var item = await TestData.FirstActiveItemAsync(fixture);

        Assert.True(await _saved.ToggleAsync(user.Id, item.Id));
        Assert.True(await _saved.IsSavedAsync(user.Id, item.Id));
        Assert.Single(await _saved.ListAsync(user.Id));

        Assert.False(await _saved.ToggleAsync(user.Id, item.Id));
        Assert.False(await _saved.IsSavedAsync(user.Id, item.Id));
        Assert.Empty(await _saved.ListAsync(user.Id));
    }

    [Fact]
    public async Task Saved_lists_are_per_user()
    {
        var first = await TestData.NewClientAsync(fixture, _auth);
        var second = await TestData.NewClientAsync(fixture, _auth);
        var item = await TestData.FirstActiveItemAsync(fixture);

        await _saved.ToggleAsync(first.Id, item.Id);

        Assert.Single(await _saved.ListAsync(first.Id));
        Assert.Empty(await _saved.ListAsync(second.Id));
    }
}
