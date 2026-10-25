namespace ShopTest.Core.Domain;

/// <summary>Lifecycle of an order. Transitions are validated by <c>OrderService</c>.</summary>
public enum OrderStatus
{
    PendingPayment,
    Paid,
    PaymentFailed,
    Processing,
    Shipped,
    Delivered,
    Cancelled,
    Refunded,
}

/// <summary>State of a single payment attempt.</summary>
public enum PaymentStatus
{
    Pending,
    Authorized,
    Declined,
    Refunded,
}
