using Microsoft.Data.SqlClient;

namespace SensorDashboard.Api.Data;

/// <summary>
/// The one place that knows the connection string. ADO.NET and Dapper code asks it for
/// connections instead of each class carrying the string around.
/// </summary>
public sealed class SqlConnectionFactory(string connectionString)
{
    /// <summary>An unopened connection (Dapper opens and closes it as needed).</summary>
    public SqlConnection Create() => new(connectionString);

    public async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = Create();
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}

internal static class SqlDateTime
{
    /// <summary>Timestamps are stored as UTC datetime2; readers hand them back with Kind = Unspecified.</summary>
    public static DateTimeOffset AsUtc(this DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
