using Microsoft.EntityFrameworkCore;
using ShopTest.Core.Data;
using ShopTest.Core.Domain;

namespace ShopTest.Core.Services;

public sealed class OrderService(IDbContextFactory<ShopDbContext> factory, PaymentService payments)
{
    private static readonly Dictionary<OrderStatus, OrderStatus[]> AllowedTransitions = new()
    {
        [OrderStatus.PendingPayment] = [OrderStatus.Paid, OrderStatus.PaymentFailed, OrderStatus.Cancelled],
        [OrderStatus.PaymentFailed] = [OrderStatus.Paid, OrderStatus.Cancelled],
        [OrderStatus.Paid] = [OrderStatus.Processing, OrderStatus.Cancelled, OrderStatus.Refunded],
        [OrderStatus.Processing] = [OrderStatus.Shipped, OrderStatus.Cancelled, OrderStatus.Refunded],
        [OrderStatus.Shipped] = [OrderStatus.Delivered, OrderStatus.Refunded],
        [OrderStatus.Delivered] = [OrderStatus.Refunded],
        [OrderStatus.Cancelled] = [],
        [OrderStatus.Refunded] = [],
    };

    public static bool CanTransition(OrderStatus from, OrderStatus to) =>
        AllowedTransitions.TryGetValue(from, out var targets) && targets.Contains(to);

    public static IReadOnlyList<OrderStatus> AllowedFrom(OrderStatus from) =>
        AllowedTransitions.TryGetValue(from, out var targets) ? targets : [];

    public async Task<OrderDto> PlaceOrderAsync(
        int userId,
        string shippingAddress,
        CardDetails card,
        string? idempotencyKey = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(shippingAddress))
        {
            throw new ShopException("A shipping address is required.");
        }

        await using var db = await factory.CreateDbContextAsync(ct);

        var key = string.IsNullOrWhiteSpace(idempotencyKey) ? Guid.NewGuid().ToString("N") : idempotencyKey.Trim();

        var duplicate = await db.Payments
            .SingleOrDefaultAsync(p => p.IdempotencyKey == key, ct);

        if (duplicate is not null)
        {
            var existing = await LoadOrderAsync(db, duplicate.OrderId, ct);
            return Map(existing);
        }

        var cartItems = await db.CartItems
            .Include(c => c.Item)
            .Where(c => c.UserId == userId)
            .ToListAsync(ct);

        if (cartItems.Count == 0)
        {
            throw new ShopException("Your cart is empty.");
        }

        var subtotal = cartItems.Sum(c => c.Item!.Price * c.Quantity);
        var totals = OrderTotals.From(subtotal);

        // Validate and charge before persisting so a malformed card never leaves an order behind.
        var result = payments.Charge(totals.Total, card);

        var order = new Order
        {
            UserId = userId,
            Status = OrderStatus.PendingPayment,
            PaymentStatus = PaymentStatus.Pending,
            PlacedAt = DateTimeOffset.UtcNow,
            Subtotal = totals.Subtotal,
            Shipping = totals.Shipping,
            Tax = totals.Tax,
            Total = totals.Total,
            ShippingAddress = shippingAddress.Trim(),
        };

        foreach (var line in cartItems)
        {
            order.Lines.Add(new OrderLine
            {
                ItemId = line.ItemId,
                NameSnapshot = line.Item!.Name,
                UnitPrice = line.Item.Price,
                Quantity = line.Quantity,
            });
        }

        AddHistory(order, from: null, OrderStatus.PendingPayment, "checkout", "Order created.");

        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);

        db.Payments.Add(new Payment
        {
            OrderId = order.Id,
            Status = result.Success ? PaymentStatus.Authorized : PaymentStatus.Declined,
            Amount = order.Total,
            CardBrand = result.Brand,
            CardLast4 = result.Last4,
            Reference = result.Reference,
            IdempotencyKey = key,
            FailureReason = result.FailureReason,
        });

        if (result.Success)
        {
            order.PaymentStatus = PaymentStatus.Authorized;
            MoveTo(order, OrderStatus.Paid, "checkout", "Payment authorized.");
            db.CartItems.RemoveRange(cartItems);
        }
        else
        {
            order.PaymentStatus = PaymentStatus.Declined;
            MoveTo(order, OrderStatus.PaymentFailed, "checkout", result.FailureReason);
        }

        await db.SaveChangesAsync(ct);
        return Map(order);
    }

    public async Task<OrderDto> RetryPaymentAsync(int orderId, int userId, CardDetails card, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var order = await LoadOrderAsync(db, orderId, ct);

        if (order.UserId != userId)
        {
            throw new ShopException("Order not found.");
        }

        if (order.Status is not (OrderStatus.PaymentFailed or OrderStatus.PendingPayment))
        {
            throw new ShopException("This order cannot be paid again.");
        }

        var result = payments.Charge(order.Total, card);

        db.Payments.Add(new Payment
        {
            OrderId = order.Id,
            Status = result.Success ? PaymentStatus.Authorized : PaymentStatus.Declined,
            Amount = order.Total,
            CardBrand = result.Brand,
            CardLast4 = result.Last4,
            Reference = result.Reference,
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            FailureReason = result.FailureReason,
        });

        if (result.Success)
        {
            order.PaymentStatus = PaymentStatus.Authorized;
            MoveTo(order, OrderStatus.Paid, "customer", "Payment retried successfully.");

            var cart = await db.CartItems.Where(c => c.UserId == userId).ToListAsync(ct);
            db.CartItems.RemoveRange(cart);
        }
        else if (order.Status == OrderStatus.PendingPayment)
        {
            order.PaymentStatus = PaymentStatus.Declined;
            MoveTo(order, OrderStatus.PaymentFailed, "customer", result.FailureReason);
        }

        await db.SaveChangesAsync(ct);
        return Map(order);
    }

    public async Task<OrderDto> CancelAsync(int orderId, int userId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var order = await LoadOrderAsync(db, orderId, ct);

        if (order.UserId != userId)
        {
            throw new ShopException("Order not found.");
        }

        if (order.Status is not (OrderStatus.PendingPayment or OrderStatus.PaymentFailed or OrderStatus.Paid))
        {
            throw new ShopException("Orders can no longer be cancelled at this stage.");
        }

        if (order.Status == OrderStatus.Paid)
        {
            await RefundPaymentsAsync(db, order, ct);
            order.PaymentStatus = PaymentStatus.Refunded;
        }

        MoveTo(order, OrderStatus.Cancelled, "customer", "Cancelled by customer.");
        await db.SaveChangesAsync(ct);
        return Map(order);
    }

    /// <summary>Admin action: advance or override the lifecycle using the same transition table.</summary>
    public async Task<OrderDto> TransitionAsync(
        int orderId,
        OrderStatus target,
        string changedBy,
        string? reason = null,
        CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var order = await LoadOrderAsync(db, orderId, ct);

        if (target == OrderStatus.Refunded && order.Status != OrderStatus.Refunded)
        {
            await RefundPaymentsAsync(db, order, ct);
            order.PaymentStatus = PaymentStatus.Refunded;
        }

        MoveTo(order, target, changedBy, reason);
        await db.SaveChangesAsync(ct);
        return Map(order);
    }

    public async Task<OrderDto> GetAsync(int orderId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return Map(await LoadOrderAsync(db, orderId, ct));
    }

    public async Task<IReadOnlyList<OrderDto>> GetHistoryAsync(int userId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var orders = await db.Orders
            .Include(o => o.Lines)
            .Include(o => o.StatusHistory)
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.PlacedAt)
            .ToListAsync(ct);

        return orders.Select(Map).ToList();
    }

    public static string StatusLabel(OrderStatus status) => status switch
    {
        OrderStatus.PendingPayment => "Awaiting payment",
        OrderStatus.PaymentFailed => "Payment failed",
        OrderStatus.Paid => "Paid",
        OrderStatus.Processing => "Processing",
        OrderStatus.Shipped => "Shipped",
        OrderStatus.Delivered => "Delivered",
        OrderStatus.Cancelled => "Cancelled",
        OrderStatus.Refunded => "Refunded",
        _ => status.ToString(),
    };

    private static async Task<Order> LoadOrderAsync(ShopDbContext db, int orderId, CancellationToken ct) =>
        await db.Orders
            .Include(o => o.Lines)
            .Include(o => o.StatusHistory)
            .SingleOrDefaultAsync(o => o.Id == orderId, ct)
        ?? throw new ShopException("Order not found.");

    private static async Task RefundPaymentsAsync(ShopDbContext db, Order order, CancellationToken ct)
    {
        var authorized = await db.Payments
            .Where(p => p.OrderId == order.Id && p.Status == PaymentStatus.Authorized)
            .ToListAsync(ct);

        foreach (var payment in authorized)
        {
            payment.Status = PaymentStatus.Refunded;
        }
    }

    private static void MoveTo(Order order, OrderStatus target, string changedBy, string? reason)
    {
        if (!CanTransition(order.Status, target))
        {
            throw new ShopException($"Cannot move an order from {StatusLabel(order.Status)} to {StatusLabel(target)}.");
        }

        AddHistory(order, order.Status, target, changedBy, reason);
        order.Status = target;
    }

    private static void AddHistory(Order order, OrderStatus? from, OrderStatus to, string changedBy, string? reason)
    {
        order.StatusHistory.Add(new OrderStatusHistory
        {
            FromStatus = from,
            ToStatus = to,
            ChangedAt = DateTimeOffset.UtcNow,
            ChangedBy = changedBy,
            Reason = reason,
        });
    }

    private static OrderDto Map(Order order) => new(
        order.Id,
        order.Status,
        order.PaymentStatus,
        order.PlacedAt,
        order.Subtotal,
        order.Shipping,
        order.Tax,
        order.Total,
        order.ShippingAddress,
        order.Lines.OrderBy(l => l.Id).Select(l => new OrderLineDto(l.NameSnapshot, l.UnitPrice, l.Quantity)).ToList(),
        order.StatusHistory
            .OrderBy(h => h.ChangedAt)
            .ThenBy(h => h.Id)
            .Select(h => new OrderStatusChangeDto(h.FromStatus, h.ToStatus, h.ChangedAt, h.ChangedBy, h.Reason))
            .ToList());
}
