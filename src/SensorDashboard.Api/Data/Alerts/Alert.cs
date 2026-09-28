using IoTDigitalTwin.Contracts.Alerts;
using SensorDashboard.Api.Data.Metadata;

namespace SensorDashboard.Api.Data.Alerts;

public sealed class Alert
{
    public long Id { get; set; }

    public required string SensorId { get; set; }

    public required string WarehouseId { get; set; }

    public AlertKind Kind { get; set; }

    public AlertSeverity Severity { get; set; }

    /// <summary>When the condition began.</summary>
    public DateTimeOffset OpenedAt { get; set; }

    /// <summary>Last evaluation that still saw the condition.</summary>
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>Null while active; one active alert per sensor and kind (filtered unique index).</summary>
    public DateTimeOffset? ClosedAt { get; set; }

    public DateTimeOffset? AcknowledgedAt { get; set; }

    public string? AcknowledgedBy { get; set; }

    public DateTimeOffset? EscalatedAt { get; set; }

    public double? PeakTemperature { get; set; }

    public required string Message { get; set; }

    public Sensor Sensor { get; set; } = null!;

    public AlertState State => ClosedAt is not null
        ? AlertState.Resolved
        : AcknowledgedAt is not null ? AlertState.Acknowledged : AlertState.Open;
}
