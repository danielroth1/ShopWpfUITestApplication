using ShopTest.Core.Domain;

namespace ShopTest.Core.Services;

public sealed record ItemDto(
    int Id,
    string Name,
    string Description,
    decimal Price,
    string Category,
    int CategoryId,
    bool IsActive);

public sealed record CategoryDto(int Id, string Name, string Slug);

public sealed record CartLineDto(int ItemId, string Name, decimal UnitPrice, int Quantity)
{
    public decimal LineTotal => UnitPrice * Quantity;
}

public sealed record CartDto(IReadOnlyList<CartLineDto> Lines)
{
    public decimal Subtotal => Lines.Sum(l => l.LineTotal);
    public int ItemCount => Lines.Sum(l => l.Quantity);
}

public sealed record OrderLineDto(string Name, decimal UnitPrice, int Quantity)
{
    public decimal LineTotal => UnitPrice * Quantity;
}

public sealed record OrderStatusChangeDto(OrderStatus? From, OrderStatus To, DateTimeOffset ChangedAt, string ChangedBy, string? Reason);

public sealed record OrderDto(
    int Id,
    OrderStatus Status,
    PaymentStatus PaymentStatus,
    DateTimeOffset PlacedAt,
    decimal Subtotal,
    decimal Shipping,
    decimal Tax,
    decimal Total,
    string ShippingAddress,
    IReadOnlyList<OrderLineDto> Lines,
    IReadOnlyList<OrderStatusChangeDto> History);

public sealed record CardDetails(string Number, string Holder, string Expiry, string Cvc);

public sealed record PaymentResult(bool Success, string? FailureReason, string Reference, string Brand, string Last4);

/// <summary>Order totals calculation, shared by cart preview and checkout.</summary>
public static class OrderTotals
{
    public const decimal FreeShippingThreshold = 100m;
    public const decimal FlatShipping = 4.99m;
    public const decimal TaxRate = 0.08m;

    public static (decimal Subtotal, decimal Shipping, decimal Tax, decimal Total) From(decimal subtotal)
    {
        var shipping = subtotal >= FreeShippingThreshold || subtotal == 0m ? 0m : FlatShipping;
        var tax = Math.Round(subtotal * TaxRate, 2, MidpointRounding.AwayFromZero);
        return (subtotal, shipping, tax, subtotal + shipping + tax);
    }
}
