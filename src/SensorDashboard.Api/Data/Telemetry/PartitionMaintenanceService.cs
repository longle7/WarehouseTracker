using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using SensorDashboard.Api.Services;

namespace SensorDashboard.Api.Data.Telemetry;

/// <summary>
/// Sliding-window maintenance for telemetry.SensorReadings: keeps daily partitions created
/// ahead of incoming data and drops partitions past retention. The work itself lives in
/// telemetry.usp_MaintainReadingPartitions (TelemetryStore migration). A missed run is
/// harmless while PartitionDaysAhead > 0.
/// </summary>
public sealed class PartitionMaintenanceService(
    SqlConnectionFactory connections,
    IOptions<TelemetryOptions> options,
    TimeProvider timeProvider,
    ILogger<PartitionMaintenanceService> logger) : PeriodicBackgroundService(timeProvider, logger)
{
    private const int MaintenanceCommandTimeoutSeconds = 300;

    protected override TimeSpan Interval => options.Value.MaintenanceInterval;

    protected override async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("telemetry.usp_MaintainReadingPartitions", connection)
        {
            CommandType = CommandType.StoredProcedure,
            // Background maintenance can legitimately outlast the request-path timeout.
            CommandTimeout = MaintenanceCommandTimeoutSeconds,
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
