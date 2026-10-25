using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ShopTest.App;
using ShopTest.App.Services;
using ShopTest.App.ViewModels;
using ShopTest.UiTests.Infrastructure;
using Xunit;

namespace ShopTest.UiTests;

/// <summary>
/// Builds the container the app actually builds and resolves everything the shell asks for at runtime.
/// A missing or mis-scoped registration otherwise only surfaces when a user clicks the wrong button.
/// </summary>
/// <remarks>
/// No database connection is opened: the <c>IDbContextFactory</c> is registered, never used.
/// </remarks>
public sealed class StartupWiringTests
{
    private static readonly Type[] ResolvedAtRuntime =
    [
        typeof(SessionService),
        typeof(ShellViewModel),
        typeof(IViewNavigator),
        typeof(LoginViewModel),
        typeof(CatalogViewModel),
        typeof(CartViewModel),
        typeof(CheckoutViewModel),
        typeof(OrderHistoryViewModel),
        typeof(SavedItemsViewModel),
        typeof(AccountViewModel),
        typeof(AdminViewModel),
        typeof(MainWindow),
    ];

    [Fact]
    public void The_composition_root_resolves_every_view_model_and_the_main_window()
    {
        List<string> failures = UiHost.Invoke(() =>
        {
            using IHost host = CompositionRoot.CreateHost([]);
            var problems = new List<string>();

            foreach (Type type in ResolvedAtRuntime)
            {
                try
                {
                    host.Services.GetRequiredService(type);
                }
                catch (Exception ex)
                {
                    problems.Add($"{type.Name}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            return problems;
        });

        Assert.True(
            failures.Count == 0,
            "The container cannot resolve:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }
}
