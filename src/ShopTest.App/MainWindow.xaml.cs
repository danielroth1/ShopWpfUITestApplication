using System;
using System.Windows;
using ShopTest.App.ViewModels;

namespace ShopTest.App;

public partial class MainWindow : Window
{
    public MainWindow(ShellViewModel shell)
    {
        InitializeComponent();
        DataContext = shell;
        Loaded += (_, _) => FitToWorkArea();
    }

    /// <summary>
    /// The default 1320x860 size is larger than the work area on a 150% scaled laptop, so
    /// <see cref="WindowStartupLocation.CenterScreen"/> placed the window partly above the screen and the
    /// shell header (cart badge, burger menu) could not be reached by the user or by UI automation.
    /// </summary>
    private void FitToWorkArea()
    {
        Rect workArea = SystemParameters.WorkArea;

        MinWidth = Math.Min(MinWidth, workArea.Width);
        MinHeight = Math.Min(MinHeight, workArea.Height);
        Width = Math.Min(Width, workArea.Width);
        Height = Math.Min(Height, workArea.Height);

        Left = workArea.Left + ((workArea.Width - Width) / 2);
        Top = workArea.Top + ((workArea.Height - Height) / 2);
    }
}
