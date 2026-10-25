using System.Diagnostics;
using System.Windows;
using ShopTest.App.Services;
using ShopTest.App.ViewModels;
using ShopTest.App.Views;
using ShopTest.Core.Services;
using ShopTest.UiTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace ShopTest.UiTests;

/// <summary>
/// Proves the harness drives the real view the way a user would: an automation-peer click must reach the
/// command, update the view model, and re-render the bound view - all without a window.
/// </summary>
public sealed class LoginViewInteractionTests(ITestOutputHelper output)
{
    [Fact]
    public void Toggling_register_mode_flows_through_the_automation_peer_into_the_view_model_and_back()
    {
        var navigator = new RecordingNavigator();
        var model = new LoginViewModel(
            new AuthService(new OfflineDbContextFactory()),
            new SessionService(),
            navigator);

        var stopwatch = Stopwatch.StartNew();
        FrameworkElement view = UiHost.Invoke(() => Ui.Show(new LoginView(), model));
        output.WriteLine($"login view built and laid out in {stopwatch.Elapsed.TotalMilliseconds:0.#} ms");

        // Initial state, straight off the rendered view.
        Assert.False(UiHost.Invoke(() => Ui.IsEffectivelyVisible(view, "auth.displayName")));
        Assert.Equal("Sign in", UiHost.Invoke(() => Ui.Text(view, "auth.submit")));

        // Click the real button through its automation peer; ButtonAutomationPeer posts the click.
        stopwatch.Restart();
        UiHost.Invoke(() => Ui.Click(view, "auth.toggleMode"));
        UiHost.PumpUntil(
            () => model.IsRegisterMode,
            "clicking auth.toggleMode should put the view model into register mode");
        stopwatch.Stop();
        output.WriteLine($"click round-trip in {stopwatch.Elapsed.TotalMilliseconds:0.##} ms");

        // View model changed...
        Assert.True(model.IsRegisterMode);
        Assert.Equal("Create account", model.Title);

        // ...and the view followed, including the converter-driven visibility.
        Assert.Equal("Create account", UiHost.Invoke(() => Ui.Text(view, "auth.submit")));
        Assert.True(UiHost.Invoke(() => Ui.IsEffectivelyVisible(view, "auth.displayName")));

        // Typing through the value pattern reaches the bound property.
        UiHost.Invoke(() => Ui.SetText(view, "auth.email", "client@shop.local"));
        UiHost.PumpUntil(
            () => model.Email == "client@shop.local",
            "typing into auth.email should update the Email property");
    }

    [Fact]
    public void The_demo_client_credentials_are_prefilled_in_the_rendered_form()
    {
        var model = new LoginViewModel(
            new AuthService(new OfflineDbContextFactory()),
            new SessionService(),
            new RecordingNavigator());

        FrameworkElement view = UiHost.Invoke(() => Ui.Show(new LoginView(), model));

        Assert.Equal(Core.Data.DbSeeder.DemoClientEmail, UiHost.Invoke(() => Ui.Text(view, "auth.email")));
        Assert.Equal(Core.Data.DbSeeder.DemoClientPassword, UiHost.Invoke(() => Ui.Text(view, "auth.password")));
    }

    [Fact]
    public void The_harness_never_shows_a_window()
    {
        // A *shown* window is what would cover the screen or take focus. Constructing one is harmless -
        // StartupWiringTests does that to resolve MainWindow - showing one is what must never happen.
        List<Window> visible = UiHost.Invoke(() =>
            Application.Current.Windows.Cast<Window>().Where(w => w.IsVisible).ToList());

        Assert.Empty(visible);
    }
}
