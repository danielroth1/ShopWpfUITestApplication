using Microsoft.EntityFrameworkCore;
using ShopTest.Core.Domain;
using ShopTest.Core.Security;
using ShopTest.Core.Services;

namespace ShopTest.Core.Data;

/// <summary>
/// Idempotent seed data. Runs at app start, in the database gate and in tests, so every
/// environment starts from the same catalog.
/// </summary>
public static class DbSeeder
{
    public const string AdminEmail = "admin@shop.local";
    public const string AdminPassword = "Admin123!";
    public const string DemoClientEmail = "client@shop.local";
    public const string DemoClientPassword = "Client123!";

    private static readonly (string Name, string Slug)[] Categories =
    [
        ("Books", "books"),
        ("Electronics", "electronics"),
        ("Fashion", "fashion"),
        ("Home", "home"),
        ("Sports", "sports"),
    ];

    private static readonly (string Name, string Description, decimal Price, string Category)[] Items =
    [
        ("Atlas of Quiet Places", "Illustrated guide to remote landscapes.", 32.50m, "books"),
        ("Bread & Butter", "A practical guide to slow baking.", 18.90m, "books"),
        ("Code Review Patterns", "How teams keep code healthy.", 41.00m, "books"),
        ("Deep Fields", "Photography of the night sky.", 27.40m, "books"),
        ("Everyday Algorithms", "Algorithms explained with household examples.", 36.75m, "books"),
        ("Field Notes Handbook", "Small notebook companion for field work.", 9.99m, "books"),
        ("Aurora Headphones", "Wireless over-ear headphones.", 189.00m, "electronics"),
        ("Beacon Speaker", "Compact Bluetooth speaker.", 74.50m, "electronics"),
        ("Circuit Keyboard", "Mechanical keyboard with quiet switches.", 129.00m, "electronics"),
        ("Dock Station Pro", "USB-C docking station with dual display.", 159.99m, "electronics"),
        ("Echo Microphone", "USB condenser microphone.", 99.00m, "electronics"),
        ("Flash Drive Duo", "128 GB dual-interface flash drive.", 24.99m, "electronics"),
        ("Glow Desk Lamp", "Dimmable LED desk lamp.", 45.00m, "electronics"),
        ("Halo Webcam", "1080p webcam with privacy shutter.", 68.00m, "electronics"),
        ("Canvas Field Jacket", "Water-resistant cotton jacket.", 142.00m, "fashion"),
        ("Drift Sneakers", "Lightweight everyday sneakers.", 89.95m, "fashion"),
        ("Everden Scarf", "Merino scarf in charcoal.", 54.00m, "fashion"),
        ("Fold Travel Bag", "Packable weekender bag.", 118.00m, "fashion"),
        ("Grain Leather Belt", "Full-grain leather belt.", 62.00m, "fashion"),
        ("Harbor Rain Coat", "Sealed seams, breathable shell.", 176.50m, "fashion"),
        ("Amber Candle Set", "Three soy candles, warm notes.", 29.00m, "home"),
        ("Birch Cutting Board", "Solid birch, oiled finish.", 38.50m, "home"),
        ("Copper Kettle", "Stovetop kettle with copper body.", 84.00m, "home"),
        ("Dune Throw Blanket", "Cotton throw, sand colour.", 49.00m, "home"),
        ("Eucalyptus Diffuser", "Ceramic diffuser with timer.", 56.00m, "home"),
        ("Fern Planter", "Stoneware planter with drainage.", 26.00m, "home"),
        ("Alpine Water Bottle", "Insulated 750 ml bottle.", 34.00m, "sports"),
        ("Balance Mat", "Cork yoga mat.", 72.00m, "sports"),
        ("Climb Chalk Bag", "Chalk bag with belt loop.", 21.50m, "sports"),
        ("Drift Running Vest", "Reflective running vest.", 58.00m, "sports"),
        ("Endurance Trainer", "Resistance bands set.", 39.99m, "sports"),
    ];

    public static async Task SeedAsync(ShopDbContext db, CancellationToken ct = default)
    {
        await SeedCatalogAsync(db, ct);
        await SeedDemoUsersAsync(db, ct);
        await SeedDemoOrderAsync(db, ct);
    }

    public static async Task<int> SeedCatalogAsync(ShopDbContext db, CancellationToken ct = default)
    {
        var categoryMap = new Dictionary<string, Category>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, slug) in Categories)
        {
            var category = await db.Categories.SingleOrDefaultAsync(c => c.Slug == slug, ct);
            if (category is null)
            {
                category = new Category { Name = name, Slug = slug };
                db.Categories.Add(category);
            }

            categoryMap[slug] = category;
        }

        await db.SaveChangesAsync(ct);

        var existingNames = await db.Items.Select(i => i.Name).ToListAsync(ct);
        var known = existingNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = 0;

        foreach (var (name, description, price, slug) in Items)
        {
            if (known.Contains(name))
            {
                continue;
            }

            db.Items.Add(new Item
            {
                Name = name,
                Description = description,
                Price = price,
                CategoryId = categoryMap[slug].Id,
            });

            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return added;
    }

    public static async Task<int> SeedDemoUsersAsync(ShopDbContext db, CancellationToken ct = default)
    {
        var added = 0;

        if (!await db.Users.AnyAsync(u => u.Email == AdminEmail, ct))
        {
            db.Users.Add(new User
            {
                Email = AdminEmail,
                DisplayName = "Shop Admin",
                PasswordHash = PasswordHasher.Hash(AdminPassword),
                IsAdmin = true,
            });
            added++;
        }

        if (!await db.Users.AnyAsync(u => u.Email == DemoClientEmail, ct))
        {
            db.Users.Add(new User
            {
                Email = DemoClientEmail,
                DisplayName = "Demo Client",
                PasswordHash = PasswordHasher.Hash(DemoClientPassword),
                IsAdmin = false,
            });
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return added;
    }

    private static async Task SeedDemoOrderAsync(ShopDbContext db, CancellationToken ct)
    {
        var client = await db.Users.SingleOrDefaultAsync(u => u.Email == DemoClientEmail, ct);
        if (client is null || await db.Orders.AnyAsync(o => o.UserId == client.Id, ct))
        {
            return;
        }

        var items = await db.Items.OrderBy(i => i.Id).Take(2).ToListAsync(ct);
        if (items.Count == 0)
        {
            return;
        }

        var subtotal = items.Sum(i => i.Price);
        var totals = OrderTotals.From(subtotal);
        var placedAt = DateTimeOffset.UtcNow.AddDays(-9);

        var order = new Order
        {
            UserId = client.Id,
            Status = OrderStatus.Delivered,
            PaymentStatus = PaymentStatus.Authorized,
            PlacedAt = placedAt,
            Subtotal = totals.Subtotal,
            Shipping = totals.Shipping,
            Tax = totals.Tax,
            Total = totals.Total,
            ShippingAddress = "12 Harbour Road, 2000 Sydney",
        };

        foreach (var item in items)
        {
            order.Lines.Add(new OrderLine
            {
                ItemId = item.Id,
                NameSnapshot = item.Name,
                UnitPrice = item.Price,
                Quantity = 1,
            });
        }

        var timeline = new (OrderStatus? From, OrderStatus To, int Minutes, string By, string Reason)[]
        {
            (null, OrderStatus.PendingPayment, 0, "system", "Demo order created."),
            (OrderStatus.PendingPayment, OrderStatus.Paid, 1, "system", "Payment authorized."),
            (OrderStatus.Paid, OrderStatus.Processing, 60, "admin", "Packing started."),
            (OrderStatus.Processing, OrderStatus.Shipped, 300, "admin", "Handed to carrier."),
            (OrderStatus.Shipped, OrderStatus.Delivered, 1500, "admin", "Delivered to customer."),
        };

        foreach (var (from, to, minutes, by, reason) in timeline)
        {
            order.StatusHistory.Add(new OrderStatusHistory
            {
                FromStatus = from,
                ToStatus = to,
                ChangedAt = placedAt.AddMinutes(minutes),
                ChangedBy = by,
                Reason = reason,
            });
        }

        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);

        db.Payments.Add(new Payment
        {
            OrderId = order.Id,
            Status = PaymentStatus.Authorized,
            Amount = order.Total,
            CardBrand = "Visa",
            CardLast4 = "4242",
            Reference = $"PAY-SEED{order.Id:D4}",
            IdempotencyKey = $"seed-order-{order.Id}",
        });

        await db.SaveChangesAsync(ct);
    }
}
