using CommunityToolkit.Mvvm.ComponentModel;
using ShopTest.Core.Services;

namespace ShopTest.App.ViewModels;

/// <summary>
/// One catalog card: the item plus the viewer-specific state its template renders. Keeping
/// <see cref="IsSaved"/> here lets the star flip without reloading the whole grid.
/// </summary>
public sealed partial class CatalogItemViewModel(ItemDto item) : ObservableObject
{
    public ItemDto Item { get; } = item;

    public int Id => Item.Id;

    public string Name => Item.Name;

    public string Category => Item.Category;

    public string Description => Item.Description;

    public decimal Price => Item.Price;

    [ObservableProperty]
    private bool isSaved;
}
