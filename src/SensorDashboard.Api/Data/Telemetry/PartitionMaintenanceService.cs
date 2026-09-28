using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace SensorDashboard.Api.Data.Telemetry;

/// <summary>
/// Sliding-window maintenance for telemetry.SensorReadings: keeps daily partitions created
/// ahead of incoming data and drops partitions past retention. The work itself lives in
/// telemetry.usp_MaintainReadingPartitions (TelemetryStore migration).
/// </summary>
public sealed class PartitionMaintenanceService(
    string connectionString,
    IOptions<TelemetryOptions> options,
    TimeProvider timeProvider,
    ILogger<PartitionMaintenanceService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.MaintenanceInterval, timeProvider);

        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Missing one run is harmless while PartitionDaysAhead > 0; try again next interval.
                logger.LogError(ex, "Reading partition maintenance failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand("telemetry.usp_MaintainReadingPartitions", connection)
        {
            CommandType = CommandType.StoredProcedure,
        };
        command.Parameters.AddWithValue("@DaysAhead", options.Value.PartitionDaysAhead);
        command.Parameters.AddWithValue("@RetentionDays", options.Value.RetentionDays);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        logger.LogInformation(
            "Reading partitions maintained: {Split} created, {Dropped} dropped, {Partitions} total",
            reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
    }
}
