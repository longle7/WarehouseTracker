using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace SensorDashboard.Api.Data.Telemetry;

/// <summary>
/// Keeps telemetry.SensorReadings1m current: every <see cref="TelemetryOptions.RollupRefreshInterval"/>
/// it recomputes the minutes the writer marked dirty (draining any backlog), then trims rollups
/// past their retention. Safe on several instances: dirty rows are claimed with READPAST.
/// </summary>
public sealed class RollupService(
    string connectionString,
    IOptions<TelemetryOptions> options,
    TimeProvider timeProvider,
    ILogger<RollupService> logger) : BackgroundService
{
    private const int MaxBucketsPerRun = 5000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.RollupRefreshInterval, timeProvider);
        do
        {
            try
            {
                await RefreshAsync(stoppingToken);
                await TrimAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Dirty marks stay put, so the next run picks up where this one failed.
                logger.LogWarning(ex, "Rollup refresh failed; retrying next interval");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <returns>Minutes recomputed.</returns>
    public async Task<int> RefreshAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var total = 0;
        int refreshed;
        do
        {
            await using var command = new SqlCommand("telemetry.usp_RefreshRollups", connection)
            {
                CommandType = CommandType.StoredProcedure,
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
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "DELETE TOP (10000) FROM telemetry.SensorReadings1m WHERE BucketStart < @Cutoff;", connection);
        command.Parameters.Add("@Cutoff", SqlDbType.DateTime2).Value =
            (timeProvider.GetUtcNow() - TimeSpan.FromDays(options.Value.RollupRetentionDays)).UtcDateTime;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
