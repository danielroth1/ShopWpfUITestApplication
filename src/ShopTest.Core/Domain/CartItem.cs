namespace ShopTest.Core.Domain;

public class CartItem
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int ItemId { get; set; }
    public Item? Item { get; set; }
    public int Quantity { get; set; } = 1;
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}
