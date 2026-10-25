using System.Diagnostics;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ShopTest.App;
using ShopTest.App.Views;
using ShopTest.UiTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace ShopTest.UiTests;

/// <summary>
/// Machine-checks <c>.github/instructions/wpf-ui-automation.instructions.md</c> across every screen:
/// each authored interactive control must carry a non-empty, unique
/// <c>AutomationProperties.AutomationId</c>.
/// </summary>
/// <remarks>
/// This is what <c>scripts\tools\ui-probe.ps1</c> does for one screen at a time over UI Automation. Here it
/// covers every screen in one layout pass, with no window and no desktop, so it is cheap enough to run on
/// every change.
/// </remarks>
public sealed class AutomationIdContractTests(ITestOutputHelper output)
{
    private static IHost? shellHost;

    private static readonly (string Name, Func<FrameworkElement> Create)[] Screens =
    [
        ("AccountView", () => new AccountView()),
        ("AdminView", () => new AdminView()),
        ("CartView", () => new CartView()),
        ("CatalogView", () => new CatalogView()),
        ("CheckoutView", () => new CheckoutView()),
        ("LoginView", () => new LoginView()),
        ("OrderConfirmationView", () => new OrderConfirmationView()),
        ("OrderHistoryView", () => new OrderHistoryView()),
        ("SavedItemsView", () => new SavedItemsView()),
        ("MainWindow (shell)", ShellContent),
    ];

    [Fact]
    public void Every_interactive_control_exposes_a_unique_automation_id()
    {
        var stopwatch = Stopwatch.StartNew();
        var violations = new List<string>();
        int inspected = 0;

        foreach ((string name, Func<FrameworkElement> create) in Screens)
        {
            ContractReport report = UiHost.Invoke(() => AutomationContract.Check(name, Ui.Show(create())));
            violations.AddRange(report.Violations);
            inspected += report.Authored;
            output.WriteLine($"{name,-24} {report.Authored,3} authored, {report.TemplateParts,3} template parts");
        }

        stopwatch.Stop();
        output.WriteLine(
            $"swept {Screens.Length} screens / {inspected} authored controls in {stopwatch.Elapsed.TotalMilliseconds:0.#} ms");

        Assert.True(
            violations.Count == 0,
            "AutomationId contract violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// The shell and burger markup lives in <c>MainWindow</c>, which needs the real container. Its content
    /// is laid out directly, so the Window itself is constructed but never shown.
    /// </summary>
    private static FrameworkElement ShellContent()
    {
        shellHost ??= CompositionRoot.CreateHost([]);
        var window = shellHost.Services.GetRequiredService<MainWindow>();
        return (FrameworkElement)window.Content;
    }
}
