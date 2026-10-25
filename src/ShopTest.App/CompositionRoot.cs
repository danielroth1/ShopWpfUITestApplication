using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ShopTest.App.Services;
using ShopTest.App.ViewModels;
using ShopTest.Core.Data;
using ShopTest.Core.Services;

namespace ShopTest.App;

/// <summary>
/// The application's composition root, kept out of <see cref="App"/> so that tests can build the very same
/// container instead of maintaining a hand-copied list of registrations that silently drifts.
/// </summary>
public static class CompositionRoot
{
    /// <summary>
    /// Builds the host the app runs on. Nothing here opens a database connection: the factory is
    /// registered, not resolved, so this is safe to call from tests without PostgreSQL running.
    /// </summary>
    public static IHost CreateHost(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices(ConfigureServices)
            .Build();

    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContextFactory<ShopDbContext>(options =>
            options.UseNpgsql(DbConnectionInfo.Current));

        services.AddSingleton<PaymentService>();
        services.AddSingleton<AuthService>();
        services.AddSingleton<CatalogService>();
        services.AddSingleton<CartService>();
        services.AddSingleton<SavedItemsService>();
        services.AddSingleton<OrderService>();
        services.AddSingleton<AdminService>();

        services.AddSingleton<SessionService>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<IViewNavigator>(sp => sp.GetRequiredService<ShellViewModel>());

        services.AddTransient<LoginViewModel>();
        services.AddTransient<CatalogViewModel>();
        services.AddTransient<CartViewModel>();
        services.AddTransient<CheckoutViewModel>();
        services.AddTransient<OrderHistoryViewModel>();
        services.AddTransient<SavedItemsViewModel>();
        services.AddTransient<AccountViewModel>();
        services.AddTransient<AdminViewModel>();

        services.AddSingleton<MainWindow>();
    }
}
