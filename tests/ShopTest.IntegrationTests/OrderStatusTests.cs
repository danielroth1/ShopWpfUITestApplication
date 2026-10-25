using Microsoft.EntityFrameworkCore;
using ShopTest.Core.Domain;
using ShopTest.Core.Services;
using Xunit;

namespace ShopTest.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class OrderStatusTests(DatabaseFixture fixture)
{
    private readonly AuthService _auth = new(fixture.Factory);
    private readonly CartService _cart = new(fixture.Factory);
    private readonly OrderService _orders = new(fixture.Factory, new PaymentService());

    [Fact]
    public async Task Order_walks_the_full_fulfilment_path()
    {
        var (user, order) = await PaidOrderAsync();

        await _orders.TransitionAsync(order.Id, OrderStatus.Processing, "admin");
        var delivered = await _orders.TransitionAsync(order.Id, OrderStatus.Shipped, "admin");
        delivered = await _orders.TransitionAsync(order.Id, OrderStatus.Delivered, "admin");

        Assert.Equal(OrderStatus.Delivered, delivered.Status);
        Assert.Equal(5, delivered.History.Count);

        var history = await _orders.GetHistoryAsync(user.Id);
        Assert.Single(history);
        Assert.Equal(OrderStatus.Delivered, history[0].Status);
    }

    [Fact]
    public async Task Paid_cannot_jump_straight_to_delivered()
    {
        var (_, order) = await PaidOrderAsync();

        await Assert.ThrowsAsync<ShopException>(() =>
            _orders.TransitionAsync(order.Id, OrderStatus.Delivered, "admin"));

        var stillPaid = await _orders.GetAsync(order.Id);
        Assert.Equal(OrderStatus.Paid, stillPaid.Status);
    }

    [Fact]
    public async Task Cancelling_a_paid_order_refunds_the_payment()
    {
        var (user, order) = await PaidOrderAsync();

        var cancelled = await _orders.CancelAsync(order.Id, user.Id);

        Assert.Equal(OrderStatus.Cancelled, cancelled.Status);
        Assert.Equal(PaymentStatus.Refunded, cancelled.PaymentStatus);

        await using var db = await fixture.Factory.CreateDbContextAsync();
        var payment = await db.Payments.SingleAsync(p => p.OrderId == order.Id);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
    }

    [Fact]
    public async Task Delivered_orders_cannot_be_cancelled_by_the_client()
    {
        var (user, order) = await PaidOrderAsync();
        await _orders.TransitionAsync(order.Id, OrderStatus.Processing, "admin");
        await _orders.TransitionAsync(order.Id, OrderStatus.Shipped, "admin");
        await _orders.TransitionAsync(order.Id, OrderStatus.Delivered, "admin");

        await Assert.ThrowsAsync<ShopException>(() => _orders.CancelAsync(order.Id, user.Id));
    }

    [Fact]
    public async Task Admin_can_refund_a_delivered_order()
    {
        var (_, order) = await PaidOrderAsync();
        await _orders.TransitionAsync(order.Id, OrderStatus.Processing, "admin");
        await _orders.TransitionAsync(order.Id, OrderStatus.Shipped, "admin");
        await _orders.TransitionAsync(order.Id, OrderStatus.Delivered, "admin");

        var refunded = await _orders.TransitionAsync(order.Id, OrderStatus.Refunded, "admin", "Customer complaint.");

        Assert.Equal(OrderStatus.Refunded, refunded.Status);
        Assert.Equal(PaymentStatus.Refunded, refunded.PaymentStatus);
    }

    [Fact]
    public async Task A_failed_payment_can_be_retried_and_succeeds()
    {
        var user = await TestData.NewClientAsync(fixture, _auth);
        var item = await TestData.FirstActiveItemAsync(fixture);
        await _cart.AddAsync(user.Id, item.Id);

        var failed = await _orders.PlaceOrderAsync(user.Id, "1 Test Street", TestData.DeclineCard);
        Assert.Equal(OrderStatus.PaymentFailed, failed.Status);

        var paid = await _orders.RetryPaymentAsync(failed.Id, user.Id, TestData.SuccessCard);

        Assert.Equal(OrderStatus.Paid, paid.Status);
        Assert.Equal(PaymentStatus.Authorized, paid.PaymentStatus);
        Assert.Empty((await _cart.GetAsync(user.Id)).Lines);

        await using var db = await fixture.Factory.CreateDbContextAsync();
        var payments = await db.Payments.Where(p => p.OrderId == failed.Id).ToListAsync();
        Assert.Equal(2, payments.Count);
        Assert.Contains(payments, p => p.Status == PaymentStatus.Declined);
        Assert.Contains(payments, p => p.Status == PaymentStatus.Authorized);
    }

    [Fact]
    public async Task Customers_cannot_cancel_someone_elses_order()
    {
        var (_, order) = await PaidOrderAsync();
        var intruder = await TestData.NewClientAsync(fixture, _auth);

        await Assert.ThrowsAsync<ShopException>(() => _orders.CancelAsync(order.Id, intruder.Id));
    }

    private async Task<(User User, OrderDto Order)> PaidOrderAsync()
    {
        var user = await TestData.NewClientAsync(fixture, _auth);
        var item = await TestData.FirstActiveItemAsync(fixture);
        await _cart.AddAsync(user.Id, item.Id);

        var order = await _orders.PlaceOrderAsync(user.Id, "1 Test Street", TestData.SuccessCard);
        Assert.Equal(OrderStatus.Paid, order.Status);
        return (user, order);
    }
}
