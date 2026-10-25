using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ShopTest.App.Services;
using ShopTest.App.ViewModels;
using ShopTest.Core.Data;
using ShopTest.Core.Services;

namespace ShopTest.App;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _host = Host.CreateDefaultBuilder(e.Args)
                .ConfigureServices(services =>
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
                })
                .Build();

            await _host.StartAsync();
            await PrepareDatabaseAsync();

            var window = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            await _host.Services.GetRequiredService<ShellViewModel>().ShowLoginAsync();
            window.Show();        }
        catch (Exception ex)
        {
            MessageBox.Show($"The shop could not start.\n\n{ex}", "ShopTest", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private async Task PrepareDatabaseAsync()
    {
        var factory = _host!.Services.GetRequiredService<IDbContextFactory<ShopDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
        await DbSeeder.SeedAsync(db);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
