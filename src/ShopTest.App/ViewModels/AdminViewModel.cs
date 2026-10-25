using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopTest.Core.Domain;
using ShopTest.Core.Services;

namespace ShopTest.App.ViewModels;

public sealed partial class AdminViewModel : ViewModelBase
{
    private readonly AdminService _admin;
    private readonly OrderService _orders;

    [ObservableProperty]
    private string newItemName = string.Empty;

    [ObservableProperty]
    private string newItemDescription = string.Empty;

    [ObservableProperty]
    private string newItemPrice = "19.99";

    [ObservableProperty]
    private CategoryDto? newItemCategory;

    [ObservableProperty]
    private string? statusMessage;

    public AdminViewModel(AdminService admin, OrderService orders)
    {
        _admin = admin;
        _orders = orders;
    }

    public override string Title => "Admin panel";

    public ObservableCollection<CategoryDto> Categories { get; } = new();

    public ObservableCollection<ItemDto> Items { get; } = new();

    public ObservableCollection<AdminOrderRowViewModel> Orders { get; } = new();

    public override async Task LoadAsync() => await ReloadAsync();

    [RelayCommand]
    private async Task SeedItemsAsync()
    {
        var added = await _admin.SeedItemsAsync();
        StatusMessage = added == 0 ? "Catalog already seeded." : $"{added} items seeded.";
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task SeedUsersAsync()
    {
        var added = await _admin.SeedUsersAsync();
        StatusMessage = added == 0 ? "Demo users already exist." : $"{added} demo users created.";
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task AddItemAsync()
    {
        if (!decimal.TryParse(NewItemPrice, NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
        {
            StatusMessage = "Enter the price as a number, for example 19.99.";
            return;
        }

        try
        {
            var item = await _admin.AddItemAsync(NewItemName, NewItemDescription, price, NewItemCategory?.Id ?? 0);
            StatusMessage = $"{item.Name} added.";
            NewItemName = string.Empty;
            NewItemDescription = string.Empty;
            await ReloadAsync();
        }
        catch (ShopException ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task RemoveItemAsync(ItemDto? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            await _admin.RemoveItemAsync(item.Id);
            StatusMessage = $"{item.Name} removed from the catalog.";
            await ReloadAsync();
        }
        catch (ShopException ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync() => await ReloadAsync();

    private async Task ReloadAsync()
    {
        Categories.Clear();
        foreach (var category in await _admin.GetCategoriesAsync())
        {
            Categories.Add(category);
        }

        NewItemCategory = Categories.FirstOrDefault();

        Items.Clear();
        foreach (var item in await _admin.ListItemsAsync())
        {
            Items.Add(item);
        }

        Orders.Clear();
        foreach (var order in await _admin.ListOrdersAsync())
        {
            Orders.Add(new AdminOrderRowViewModel(order, ApplyStatusAsync));
        }
    }

    private async Task ApplyStatusAsync(AdminOrderRowViewModel row)
    {
        if (row.SelectedChoice is null)
        {
            return;
        }

        try
        {
            await _orders.TransitionAsync(row.Order.Id, row.SelectedChoice.Status, "admin", "Status updated from the admin panel.");
            StatusMessage = $"Order #{row.Order.Id} moved to {row.SelectedChoice.Label}.";
        }
        catch (ShopException ex)
        {
            StatusMessage = ex.Message;
        }

        await ReloadAsync();
    }
}

public sealed record StatusChoice(OrderStatus Status, string Label);

public sealed partial class AdminOrderRowViewModel : ObservableObject
{
    private readonly Func<AdminOrderRowViewModel, Task> _apply;

    public AdminOrderRowViewModel(OrderDto order, Func<AdminOrderRowViewModel, Task> apply)
    {
        Order = order;
        _apply = apply;
        Choices = OrderService.AllowedFrom(order.Status)
            .Select(status => new StatusChoice(status, OrderService.StatusLabel(status)))
            .ToList();
        SelectedChoice = Choices.FirstOrDefault();
    }

    public OrderDto Order { get; }

    public IReadOnlyList<StatusChoice> Choices { get; }

    [ObservableProperty]
    private StatusChoice? selectedChoice;

    public string Header => $"Order #{Order.Id} · {Order.Total:C}";

    public string StatusText => OrderService.StatusLabel(Order.Status);

    public bool CanAdvance => Choices.Count > 0;

    [RelayCommand]
    private Task ApplyAsync() => _apply(this);
}
