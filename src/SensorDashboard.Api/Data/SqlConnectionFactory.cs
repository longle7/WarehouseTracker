using Microsoft.Data.SqlClient;

namespace SensorDashboard.Api.Data;

/// <summary>
/// The one place that knows the connection string. ADO.NET and Dapper code asks it for
/// connections instead of each class carrying the string around.
/// </summary>
public sealed class SqlConnectionFactory(string connectionString, int commandTimeoutSeconds = 15)
{
    // Default timeout for every command on these connections, so a slow query fails the
    // request instead of holding it for SqlClient's 30 s default. Long-running maintenance
    // commands set their own.
    private readonly string _connectionString =
        new SqlConnectionStringBuilder(connectionString) { CommandTimeout = commandTimeoutSeconds }.ConnectionString;

    // Opening a connection retries transient failures (failover, throttling, a restarting
    // server) with exponential backoff. Commands are not retried here: writes are made safe
    // to repeat by the ingest queue and idempotent inserts instead.
    private static readonly SqlRetryLogicBaseProvider OpenRetry =
        SqlConfigurableRetryFactory.CreateExponentialRetryProvider(new SqlRetryLogicOption
        {
            NumberOfTries = 4,
            DeltaTime = TimeSpan.FromMilliseconds(500),
            MaxTimeInterval = TimeSpan.FromSeconds(5),
        });

    /// <summary>An unopened connection (Dapper opens and closes it as needed).</summary>
    /// <param name="retryOpen">False for probes like the health check, which should report an outage promptly.</param>
    public SqlConnection Create(bool retryOpen = true) =>
        new(_connectionString) { RetryLogicProvider = retryOpen ? OpenRetry : null };

    public async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken, bool retryOpen = true)
    {
        var connection = Create(retryOpen);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}

internal static class SqlDateTime
{
    /// <summary>Timestamps are stored as UTC datetime2; readers hand them back with Kind = Unspecified.</summary>
    public static DateTimeOffset AsUtc(this DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
