namespace ShopTest.Core.Data;

/// <summary>
/// Single source of truth for the PostgreSQL connection used by the app, tests and tooling.
/// The gauntlet runner exports <c>SHOP_DB_CONNECTION</c> before running gates.
/// </summary>
public static class DbConnectionInfo
{
    public const int DefaultPort = 55432;

    public static string Current =>
        Environment.GetEnvironmentVariable("SHOP_DB_CONNECTION")
        ?? Build(
            Environment.GetEnvironmentVariable("SHOP_DB_HOST") ?? "127.0.0.1",
            Environment.GetEnvironmentVariable("SHOP_DB_PORT") ?? DefaultPort.ToString(),
            Environment.GetEnvironmentVariable("SHOP_DB_NAME") ?? "shop",
            Environment.GetEnvironmentVariable("SHOP_DB_USER") ?? "shop",
            Environment.GetEnvironmentVariable("SHOP_DB_PASSWORD") ?? "shop");

    public static string Build(string host, string port, string database, string user, string password) =>
        $"Host={host};Port={port};Database={database};Username={user};Password={password};Include Error Detail=true";
}
