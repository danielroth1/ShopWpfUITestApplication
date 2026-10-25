using Microsoft.EntityFrameworkCore;
using ShopTest.Core.Domain;
using ShopTest.Core.Services;
using Xunit;

namespace ShopTest.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class CheckoutTests(DatabaseFixture fixture)
{
    private readonly AuthService _auth = new(fixture.Factory);
    private readonly CartService _cart = new(fixture.Factory);
    private readonly OrderService _orders = new(fixture.Factory, new PaymentService());

    [Fact]
    public async Task Successful_payment_marks_the_order_paid_and_clears_the_cart()
    {
        var (user, _, order) = await PlaceOrderAsync(TestData.SuccessCard);

        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(PaymentStatus.Authorized, order.PaymentStatus);
        Assert.Equal(2, order.History.Count);
        Assert.Empty((await _cart.GetAsync(user.Id)).Lines);
    }

    [Fact]
    public async Task Declined_payment_keeps_the_cart_and_records_the_failure()
    {
        var (user, _, order) = await PlaceOrderAsync(TestData.DeclineCard);

        Assert.Equal(OrderStatus.PaymentFailed, order.Status);
        Assert.Equal(PaymentStatus.Declined, order.PaymentStatus);
        Assert.Single((await _cart.GetAsync(user.Id)).Lines);

        await using var db = await fixture.Factory.CreateDbContextAsync();
        var payment = await db.Payments.SingleAsync(p => p.OrderId == order.Id);
        Assert.Equal(PaymentStatus.Declined, payment.Status);
        Assert.Contains("declined", payment.FailureReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Insufficient_funds_is_reported_as_a_decline()
    {
        var (_, _, order) = await PlaceOrderAsync(TestData.InsufficientFundsCard);

        Assert.Equal(OrderStatus.PaymentFailed, order.Status);
    }

    [Fact]
    public async Task Invalid_card_creates_no_order()
    {
        var user = await TestData.NewClientAsync(fixture, _auth);
        var item = await TestData.FirstActiveItemAsync(fixture);
        await _cart.AddAsync(user.Id, item.Id);

        await Assert.ThrowsAsync<ShopException>(() =>
            _orders.PlaceOrderAsync(user.Id, "1 Test Street", new CardDetails("1234 5678", "Test", "12/30", "123")));

        Assert.Empty(await _orders.GetHistoryAsync(user.Id));
    }

    [Fact]
    public async Task Empty_cart_cannot_be_checked_out()
    {
        var user = await TestData.NewClientAsync(fixture, _auth);

        await Assert.ThrowsAsync<ShopException>(() =>
            _orders.PlaceOrderAsync(user.Id, "1 Test Street", TestData.SuccessCard));
    }

    [Fact]
    public async Task Repeating_an_idempotency_key_returns_the_same_order()
    {
        var user = await TestData.NewClientAsync(fixture, _auth);
        var item = await TestData.FirstActiveItemAsync(fixture);
        await _cart.AddAsync(user.Id, item.Id);

        var key = Guid.NewGuid().ToString("N");
        var first = await _orders.PlaceOrderAsync(user.Id, "1 Test Street", TestData.SuccessCard, key);
        var second = await _orders.PlaceOrderAsync(user.Id, "1 Test Street", TestData.SuccessCard, key);

        Assert.Equal(first.Id, second.Id);

        await using var db = await fixture.Factory.CreateDbContextAsync();
        Assert.Equal(1, await db.Payments.CountAsync(p => p.IdempotencyKey == key));
    }

    [Fact]
    public async Task Order_totals_match_the_shared_rules()
    {
        var (_, _, order) = await PlaceOrderAsync(TestData.SuccessCard);
        var totals = OrderTotals.From(order.Subtotal);

        Assert.Equal(totals.Shipping, order.Shipping);
        Assert.Equal(totals.Tax, order.Tax);
        Assert.Equal(totals.Total, order.Total);
    }

    private async Task<(User User, Item Item, OrderDto Order)> PlaceOrderAsync(CardDetails card, string? key = null)
    {
        var user = await TestData.NewClientAsync(fixture, _auth);
        var item = await TestData.FirstActiveItemAsync(fixture);
        await _cart.AddAsync(user.Id, item.Id, 2);

        var order = await _orders.PlaceOrderAsync(user.Id, "1 Test Street, 2000 Sydney", card, key);
        return (user, item, order);
    }
}
