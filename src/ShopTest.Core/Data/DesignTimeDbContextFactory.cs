using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ShopTest.Core.Data;

/// <summary>Lets <c>dotnet ef</c> create the context without booting the WPF app.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ShopDbContext>
{
    public ShopDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ShopDbContext>()
            .UseNpgsql(DbConnectionInfo.Current)
            .Options;

        return new ShopDbContext(options);
    }
}
