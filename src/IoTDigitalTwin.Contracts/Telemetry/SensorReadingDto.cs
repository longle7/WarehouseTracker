using System.ComponentModel.DataAnnotations;

namespace IoTDigitalTwin.Contracts.Telemetry;

/// <summary>
/// A single telemetry reading posted by SensorSimulator to SensorDashboard.Api.
/// </summary>
/// <remarks>Ranges and lengths match the telemetry.SensorReadings column types.</remarks>
public sealed record SensorReadingDto
{
    [StringLength(32, MinimumLength = 1)]
    public required string SensorId { get; init; }

    [StringLength(32, MinimumLength = 1)]
    public required string WarehouseId { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Temperature in degrees Fahrenheit.</summary>
    [Range(-100, 200)]
    public required double Temperature { get; init; }

    /// <summary>Relative humidity as a percentage (0–100).</summary>
    [Range(0, 100)]
    public required double Humidity { get; init; }

    public required bool DoorOpen { get; init; }

    public bool IsAnomaly { get; init; }
}
