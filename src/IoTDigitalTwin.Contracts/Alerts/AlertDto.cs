namespace IoTDigitalTwin.Contracts.Alerts;

/// <summary>
/// A debounced alert with a lifecycle: opened once a condition has lasted past its grace
/// period, optionally acknowledged, escalated if critical and ignored, and resolved when the
/// condition clears. Unlike a sensor's instantaneous status, alerts are stored history.
/// </summary>
/// <param name="OpenedAt">When the condition began (the start of the streak), not when the alert was raised.</param>
/// <param name="PeakTemperature">For temperature alerts, the reading furthest from the safe range.</param>
public sealed record AlertDto(
    long Id,
    string SensorId,
    string WarehouseId,
    string Location,
    AlertKind Kind,
    AlertSeverity Severity,
    AlertState State,
    DateTimeOffset OpenedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset? AcknowledgedAt,
    string? AcknowledgedBy,
    DateTimeOffset? EscalatedAt,
    double? PeakTemperature,
    string Message);

/// <summary>Body of POST /alerts/{id}/acknowledge.</summary>
public sealed record AcknowledgeAlertDto(string? By);

public enum AlertKind
{
    TemperatureOutOfRange,
    DoorOpenTooLong,
    SensorOffline,
}

public enum AlertSeverity
{
    Warning,
    Critical,
}

public enum AlertState
{
    /// <summary>Condition ongoing, nobody has acknowledged it.</summary>
    Open,

    /// <summary>Condition ongoing, someone is on it.</summary>
    Acknowledged,

    /// <summary>Condition cleared.</summary>
    Resolved,
}
