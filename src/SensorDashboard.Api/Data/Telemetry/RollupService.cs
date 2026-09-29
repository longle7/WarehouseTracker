using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using SensorDashboard.Api.Services;

namespace SensorDashboard.Api.Data.Telemetry;

/// <summary>
/// Keeps telemetry.SensorReadings1m current: every <see cref="TelemetryOptions.RollupRefreshInterval"/>
/// it recomputes the minutes the writer marked dirty (draining any backlog), then trims rollups
/// past their retention. Safe on several instances: dirty rows are claimed with READPAST, and a
/// failed run leaves its dirty marks for the next one.
/// </summary>
public sealed class RollupService(
    SqlConnectionFactory connections,
    IOptions<TelemetryOptions> options,
    TimeProvider timeProvider,
    ILogger<RollupService> logger) : PeriodicBackgroundService(timeProvider, logger)
{
    private const int MaxBucketsPerRun = 5000;

    private const int MaintenanceCommandTimeoutSeconds = 300;

    protected override TimeSpan Interval => options.Value.RollupRefreshInterval;

    protected override async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        await RefreshAsync(cancellationToken);
        await TrimAsync(cancellationToken);
    }

    /// <returns>Minutes recomputed.</returns>
    public async Task<int> RefreshAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);

        var total = 0;
        int refreshed;
        do
        {
            await using var command = new SqlCommand("telemetry.usp_RefreshRollups", connection)
            {
                CommandType = CommandType.StoredProcedure,
                // Background maintenance can legitimately outlast the request-path timeout.
                CommandTimeout = MaintenanceCommandTimeoutSeconds,
            };
            command.Parameters.AddWithValue("@MaxBuckets", MaxBucketsPerRun);
            refreshed = (int)(await command.ExecuteScalarAsync(cancellationToken))!;
            total += refreshed;
        }
        while (refreshed == MaxBucketsPerRun && !cancellationToken.IsCancellationRequested);

        if (total > 0) logger.LogDebug("Refreshed {Count} rollup minutes", total);
        return total;
    }

    private async Task TrimAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "DELETE TOP (10000) FROM telemetry.SensorReadings1m WHERE BucketStart < @Cutoff;", connection);
        command.Parameters.Add("@Cutoff", SqlDbType.DateTime2).Value =
            (Clock.GetUtcNow() - TimeSpan.FromDays(options.Value.RollupRetentionDays)).UtcDateTime;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
