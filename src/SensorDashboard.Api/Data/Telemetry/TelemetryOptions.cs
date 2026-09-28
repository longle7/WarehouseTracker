namespace SensorDashboard.Api.Data.Telemetry;

public sealed class TelemetryOptions
{
    public const string SectionName = "Telemetry";

    /// <summary>How many future daily partitions to keep ready ahead of incoming data.</summary>
    public int PartitionDaysAhead { get; set; } = 7;

    /// <summary>Whole days of readings kept; older partitions are truncated and merged away.</summary>
    public int RetentionDays { get; set; } = 30;

    public TimeSpan MaintenanceInterval { get; set; } = TimeSpan.FromHours(6);
}
