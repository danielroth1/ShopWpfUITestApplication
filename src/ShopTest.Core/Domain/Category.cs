namespace ShopTest.Core.Domain;

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public ICollection<Item> Items { get; set; } = new List<Item>();
}
