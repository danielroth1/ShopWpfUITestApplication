namespace ShopTest.Core.Domain;

public class SavedItem
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int ItemId { get; set; }
    public Item? Item { get; set; }
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;
}
