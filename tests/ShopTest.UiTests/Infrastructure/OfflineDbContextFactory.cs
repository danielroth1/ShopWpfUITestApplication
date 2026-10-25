using Microsoft.EntityFrameworkCore;
using ShopTest.Core.Data;

namespace ShopTest.UiTests.Infrastructure;

/// <summary>
/// Database factory that fails loudly if a test accidentally reaches the data layer.
/// UI tests must not depend on PostgreSQL; a test that needs rows belongs in the integration suite.
/// </summary>
internal sealed class OfflineDbContextFactory : IDbContextFactory<ShopDbContext>
{
    public ShopDbContext CreateDbContext() => throw Unreachable();

    public Task<ShopDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => throw Unreachable();

    private static InvalidOperationException Unreachable() =>
        new("A UI test touched the database. Use ShopTest.IntegrationTests for data-layer coverage.");
}
