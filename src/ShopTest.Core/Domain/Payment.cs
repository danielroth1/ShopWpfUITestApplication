namespace ShopTest.Core.Domain;

public class Payment
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string Provider { get; set; } = "FakeGateway";
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public decimal Amount { get; set; }
    public string CardBrand { get; set; } = string.Empty;
    public string CardLast4 { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? FailureReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
