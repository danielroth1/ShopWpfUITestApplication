using System.Windows;
using ShopTest.App.ViewModels;

namespace ShopTest.App;

public partial class MainWindow : Window
{
    public MainWindow(ShellViewModel shell)
    {
        InitializeComponent();
        DataContext = shell;
    }
}
