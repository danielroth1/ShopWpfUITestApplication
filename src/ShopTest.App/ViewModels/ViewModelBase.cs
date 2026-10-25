using CommunityToolkit.Mvvm.ComponentModel;

namespace ShopTest.App.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    public abstract string Title { get; }

    public virtual Task LoadAsync() => Task.CompletedTask;
}
