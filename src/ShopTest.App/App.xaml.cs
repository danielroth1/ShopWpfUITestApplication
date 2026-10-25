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
    /// <summary>
    /// Set to <c>1</c> by the in-process UI test harness (tests/ShopTest.UiTests). The harness constructs
    /// this class to load the real App.xaml resources, and WPF raises <c>Startup</c> as soon as the
    /// dispatcher loop runs, so this opt-out keeps UI tests offline: no host, no migration, no seeding,
    /// no main window.
    /// </summary>
    public const string SkipStartupVariable = "SHOPTEST_SKIP_STARTUP";

    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (Environment.GetEnvironmentVariable(SkipStartupVariable) == "1")
        {
            return;
        }

        try
        {
            _host = CompositionRoot.CreateHost(e.Args);

            await _host.StartAsync();
            await PrepareDatabaseAsync();

            var window = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            await _host.Services.GetRequiredService<ShellViewModel>().ShowLoginAsync();
            window.Show();
        }
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
