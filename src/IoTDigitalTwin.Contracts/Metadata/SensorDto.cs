namespace IoTDigitalTwin.Contracts.Metadata;

/// <summary>
/// A sensor with its current status. Returned by GET /warehouses/{id}/sensors.
/// </summary>
/// <param name="LastReading">Most recent reading within the lookback window, or null if none.</param>
public sealed record SensorDto(
    string Id,
    string WarehouseId,
    string Location,
    decimal MinTemperatureF,
    decimal MaxTemperatureF,
    bool IsActive,
    SensorStatus Status,
    LastReadingDto? LastReading);

public sealed record LastReadingDto(
    DateTimeOffset Timestamp,
    double Temperature,
    double Humidity,
    bool DoorOpen,
    bool IsAnomaly);

/// <summary>Serialized as a camelCase string ("ok", "alert", ...).</summary>
public enum SensorStatus
{
    Ok,

    /// <summary>Latest reading is flagged as an anomaly or outside the sensor's temperature range.</summary>
    Alert,

    /// <summary>No reading within the offline threshold.</summary>
    Offline,

    /// <summary>Sensor is disabled in metadata; its readings are rejected at ingestion.</summary>
    Inactive,
}
