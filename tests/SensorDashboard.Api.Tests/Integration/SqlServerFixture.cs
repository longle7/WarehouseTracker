using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SensorDashboard.Api.Data;
using SensorDashboard.Api.Data.Telemetry;

namespace SensorDashboard.Api.Tests.Integration;

/// <summary>
/// A throwaway database on a real SQL Server, built by the app's own migrations and dropped
/// afterwards. Uses the local default instance with Windows auth unless IOT_TEST_SQL gives a
/// server connection string (e.g. a CI container). Partitioning, columnstore and DATE_BUCKET
/// rule out an in-memory provider.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    public string ConnectionString { get; }

    public SqlServerFixture()
    {
        var server = Environment.GetEnvironmentVariable("IOT_TEST_SQL")
            ?? "Server=localhost;Trusted_Connection=True;TrustServerCertificate=True";
        ConnectionString = new SqlConnectionStringBuilder(server)
        {
            InitialCatalog = $"IoTDigitalTwin_Test_{Guid.NewGuid():N}",
        }.ConnectionString;
    }

    public SqlConnectionFactory Connections => new(ConnectionString);

    public RollupService CreateRollupService() =>
        new(Connections, Options.Create(new TelemetryOptions()), TimeProvider.System, NullLogger<RollupService>.Instance);

    public DigitalTwinDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<DigitalTwinDbContext>().UseSqlServer(ConnectionString).Options);

    public async Task InitializeAsync()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await using var db = CreateDbContext();
        await db.Database.EnsureDeletedAsync();
    }

    /// <summary>
    /// Clears readings, rollups, the ingest queue and alerts so a test starts clean. The queue
    /// matters: batches another test left behind would be processed by the next test's API
    /// instance and could collide with its readings.
    /// </summary>
    public Task ResetTelemetryAsync() => ExecuteAsync(
        """
        TRUNCATE TABLE telemetry.SensorReadings; TRUNCATE TABLE telemetry.SensorReadings1m; TRUNCATE TABLE telemetry.RollupDirty;
        DELETE ingest.ReadingBatches; DELETE ingest.DeadLetters; DELETE ingest.BatchResults; DELETE ingest.IdempotencyKeys;
        DELETE ops.Alerts; DELETE ops.HealthSamples;
        """);

    public async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SQL Server";
}
