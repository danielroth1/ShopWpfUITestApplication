using ShopTest.App.Services;
using ShopTest.App.ViewModels;
using ShopTest.Core.Domain;
using ShopTest.Core.Services;

namespace ShopTest.UiTests.Infrastructure;

/// <summary>
/// View models seeded with representative data so that <c>DataTemplate</c> content is realized without a
/// database. Every collection gets at least two distinct items so that an id missing its item key collides
/// and is reported by <see cref="AutomationContract"/>.
/// </summary>
internal static class OfflineUiGraph
{
    private static readonly OfflineDbContextFactory Db = new();

    public static CartViewModel Cart(RecordingNavigator navigator)
    {
        var model = new CartViewModel(new CartService(Db), new SessionService(), navigator);
        model.Lines.Add(new CartLineDto(1, "Fountain pen", 9.99m, 2));
        model.Lines.Add(new CartLineDto(2, "Ink bottle", 4.50m, 1));
        return model;
    }

    public static CatalogViewModel Catalog(RecordingNavigator navigator)
    {
        var model = new CatalogViewModel(
            new CatalogService(Db),
            new CartService(Db),
            new SavedItemsService(Db),
            new SessionService(),
            navigator);

        // Categories are deliberately left empty: a Selector auto-selects its first item, which would fire
        // the category-changed handler and try to reload from the database.
        model.Items.Add(new CatalogItemViewModel(Item(1, "Fountain pen")));
        model.Items.Add(new CatalogItemViewModel(Item(2, "Ink bottle")));
        return model;
    }

    public static SavedItemsViewModel Saved(RecordingNavigator navigator)
    {
        var model = new SavedItemsViewModel(
            new SavedItemsService(Db),
            new CartService(Db),
            new SessionService(),
            navigator);

        model.Items.Add(Item(1, "Fountain pen"));
        model.Items.Add(Item(2, "Ink bottle"));
        return model;
    }

    public static OrderHistoryViewModel OrderHistory()
    {
        var model = new OrderHistoryViewModel(new OrderService(Db, new PaymentService()), new SessionService());
        model.Orders.Add(new OrderRowViewModel(Order(41), _ => Task.CompletedTask, _ => Task.CompletedTask));
        model.Orders.Add(new OrderRowViewModel(Order(42), _ => Task.CompletedTask, _ => Task.CompletedTask));
        return model;
    }

    public static AdminViewModel Admin()
    {
        var model = new AdminViewModel(new AdminService(Db), new OrderService(Db, new PaymentService()));
        model.Items.Add(Item(1, "Fountain pen"));
        model.Items.Add(Item(2, "Ink bottle"));
        model.Orders.Add(new AdminOrderRowViewModel(Order(41), _ => Task.CompletedTask));
        model.Orders.Add(new AdminOrderRowViewModel(Order(42), _ => Task.CompletedTask));
        return model;
    }

    private static ItemDto Item(int id, string name) =>
        new(id, name, $"{name} description", 9.99m, "Stationery", 1, true);

    private static OrderDto Order(int id) => new(
        Id: id,
        Status: OrderStatus.Paid,
        PaymentStatus: PaymentStatus.Authorized,
        PlacedAt: new DateTimeOffset(2026, 10, 20, 9, 30, 0, TimeSpan.Zero),
        Subtotal: 19.98m,
        Shipping: 0m,
        Tax: 1.60m,
        Total: 21.58m,
        ShippingAddress: "1 Test Street",
        Lines: [new OrderLineDto("Fountain pen", 9.99m, 2)],
        History: [new OrderStatusChangeDto(null, OrderStatus.Paid, new DateTimeOffset(2026, 10, 20, 9, 31, 0, TimeSpan.Zero), "seed", "paid")]);
}
