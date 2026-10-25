using Microsoft.EntityFrameworkCore;
using ShopTest.Core.Data;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ShopTest.IntegrationTests;

/// <summary>Creates the schema once per test run against the container database.</summary>
public sealed class TestDbContextFactory(DbContextOptions<ShopDbContext> options) : IDbContextFactory<ShopDbContext>
{
    public ShopDbContext CreateDbContext() => new(options);
}

public sealed class DatabaseFixture : IAsyncLifetime
{
    public IDbContextFactory<ShopDbContext> Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<ShopDbContext>()
            .UseNpgsql(DbConnectionInfo.Current)
            .Options;

        Factory = new TestDbContextFactory(options);

        await using var db = await Factory.CreateDbContextAsync();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
        await DbSeeder.SeedAsync(db);
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "database";
}
