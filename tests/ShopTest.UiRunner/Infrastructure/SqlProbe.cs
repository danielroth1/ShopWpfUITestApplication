using System.Globalization;
using Npgsql;
using ShopTest.Core.Data;
using ShopTest.UiRunner.Infrastructure;

namespace ShopTest.UiRunner.Infrastructure;

/// <summary>Runs scenario <c>sqlAssert</c> queries through the same connection the app uses.</summary>
internal static class SqlProbe
{
    public static string Scalar(string query)
    {
        try
        {
            using var connection = new NpgsqlConnection(DbConnectionInfo.Current);
            connection.Open();

            using var command = new NpgsqlCommand(query, connection);
            var value = command.ExecuteScalar();

            return ToPostgresText(value);
        }
        catch (NpgsqlException exception)
        {
            throw new RunnerException(
                $"sql assertion could not reach PostgreSQL ({exception.Message}). " +
                "In a sandbox run this usually means the host database bridge is missing.",
                infrastructure: true,
                exception);
        }
    }

    /// <summary>
    /// Formats a scalar the way <c>psql -tA</c> prints it, so scenario expectations stay written against
    /// the MCP/psql rendering ('t'/'f', invariant separators) instead of .NET formatting ('True'/'False').
    /// </summary>
    private static string ToPostgresText(object? value) => value switch
    {
        null or DBNull => string.Empty,
        bool boolean => boolean ? "t" : "f",
        DateTime dateTime => dateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        DateTimeOffset offset => offset.ToString("yyyy-MM-dd HH:mm:sszzz", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty,
        _ => value.ToString()?.Trim() ?? string.Empty,
    };
}
