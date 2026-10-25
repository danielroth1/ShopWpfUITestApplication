using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopTest.App.Services;
using ShopTest.Core.Services;

namespace ShopTest.App.ViewModels;

public sealed partial class SavedItemsViewModel : ViewModelBase
{
    private readonly SavedItemsService _saved;
    private readonly CartService _cart;
    private readonly SessionService _session;
    private readonly IViewNavigator _navigator;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private bool isEmpty = true;

    public SavedItemsViewModel(SavedItemsService saved, CartService cart, SessionService session, IViewNavigator navigator)
    {
        _saved = saved;
        _cart = cart;
        _session = session;
        _navigator = navigator;
    }

    public override string Title => "Saved items";

    public ObservableCollection<ItemDto> Items { get; } = new();

    public override async Task LoadAsync()
    {
        var items = await _saved.ListAsync(_session.UserId);
        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }

        IsEmpty = Items.Count == 0;
    }

    [RelayCommand]
    private async Task AddToCartAsync(ItemDto? item)
    {
        if (item is null)
        {
            return;
        }

        var cart = await _cart.AddAsync(_session.UserId, item.Id);
        _navigator.SetCartCount(cart.ItemCount);
        StatusMessage = $"{item.Name} added to cart.";
    }

    [RelayCommand]
    private async Task RemoveAsync(ItemDto? item)
    {
        if (item is null)
        {
            return;
        }

        await _saved.ToggleAsync(_session.UserId, item.Id);
        StatusMessage = $"{item.Name} removed from saved items.";
        await LoadAsync();
    }
}
