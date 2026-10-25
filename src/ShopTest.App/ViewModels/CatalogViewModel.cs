using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopTest.App.Services;
using ShopTest.Core.Services;

namespace ShopTest.App.ViewModels;

public sealed partial class CatalogViewModel : ViewModelBase
{
    private readonly CatalogService _catalog;
    private readonly CartService _cart;
    private readonly SavedItemsService _saved;
    private readonly SessionService _session;
    private readonly IViewNavigator _navigator;
    private bool _suppressReload;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private CategoryDto? selectedCategory;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private bool isBusy;

    public CatalogViewModel(
        CatalogService catalog,
        CartService cart,
        SavedItemsService saved,
        SessionService session,
        IViewNavigator navigator)
    {
        _catalog = catalog;
        _cart = cart;
        _saved = saved;
        _session = session;
        _navigator = navigator;
    }

    public override string Title => "Catalog";

    public ObservableCollection<CategoryDto> Categories { get; } = new();

    public ObservableCollection<ItemDto> Items { get; } = new();

    partial void OnSelectedCategoryChanged(CategoryDto? value)
    {
        if (!_suppressReload)
        {
            _ = ReloadItemsAsync();
        }
    }

    public override async Task LoadAsync()
    {
        _suppressReload = true;
        Categories.Clear();
        Categories.Add(new CategoryDto(0, "All categories", "all"));

        foreach (var category in await _catalog.GetCategoriesAsync())
        {
            Categories.Add(category);
        }

        SelectedCategory = Categories[0];
        _suppressReload = false;

        await ReloadItemsAsync();
    }

    [RelayCommand]
    private async Task SearchAsync() => await ReloadItemsAsync();

    [RelayCommand]
    private async Task ClearAsync()
    {
        SearchText = string.Empty;
        SelectedCategory = Categories.FirstOrDefault();
        await ReloadItemsAsync();
    }

    [RelayCommand]
    private async Task AddToCartAsync(ItemDto? item)
    {
        if (item is null || !_session.IsAuthenticated)
        {
            return;
        }

        try
        {
            var cart = await _cart.AddAsync(_session.UserId, item.Id);
            _navigator.SetCartCount(cart.ItemCount);
            StatusMessage = $"{item.Name} added to cart.";
        }
        catch (ShopException ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task SaveItemAsync(ItemDto? item)
    {
        if (item is null || !_session.IsAuthenticated)
        {
            return;
        }

        try
        {
            var saved = await _saved.ToggleAsync(_session.UserId, item.Id);
            StatusMessage = saved
                ? $"{item.Name} saved for later."
                : $"{item.Name} removed from saved items.";
        }
        catch (ShopException ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task OpenCartAsync() => await _navigator.ShowCartAsync();

    private async Task ReloadItemsAsync()
    {
        IsBusy = true;

        try
        {
            var categoryId = SelectedCategory is { Id: > 0 } ? SelectedCategory.Id : (int?)null;
            var results = await _catalog.SearchAsync(SearchText, categoryId);

            Items.Clear();
            foreach (var item in results)
            {
                Items.Add(item);
            }

            StatusMessage = results.Count == 0
                ? "No items match your filters."
                : $"{results.Count} item(s) found.";
        }
        catch (ShopException ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
