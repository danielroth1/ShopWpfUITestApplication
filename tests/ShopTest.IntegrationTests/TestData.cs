using Microsoft.EntityFrameworkCore;
using ShopTest.Core.Data;
using ShopTest.Core.Domain;
using ShopTest.Core.Services;

namespace ShopTest.IntegrationTests;

internal static class TestData
{
    public static readonly CardDetails SuccessCard = new("4242 4242 4242 4242", "Test Buyer", "12/30", "123");
    public static readonly CardDetails DeclineCard = new("4000 0000 0000 0002", "Test Buyer", "12/30", "123");
    public static readonly CardDetails InsufficientFundsCard = new("4000 0000 0000 9995", "Test Buyer", "12/30", "123");

    public static async Task<User> NewClientAsync(DatabaseFixture fixture, AuthService auth)
    {
        var email = $"client-{Guid.NewGuid():N}@shop.local";
        return await auth.RegisterAsync(email, "Password123!", "Test Client");
    }

    public static async Task<Item> FirstActiveItemAsync(DatabaseFixture fixture)
    {
        await using var db = await fixture.Factory.CreateDbContextAsync();
        return await db.Items.Where(i => i.IsActive).OrderBy(i => i.Id).FirstAsync();
    }

    public static async Task<Order> LoadOrderAsync(DatabaseFixture fixture, int orderId)
    {
        await using var db = await fixture.Factory.CreateDbContextAsync();
        return await db.Orders
            .Include(o => o.Lines)
            .Include(o => o.StatusHistory)
            .SingleAsync(o => o.Id == orderId);
    }
}
