namespace IoTDigitalTwin.Contracts.Telemetry;

/// <summary>
/// One measured property of a sensor over time, in the style of an IoT TwinMaker property
/// history. GET /sensors/{sensorId}/properties returns one per property: temperature,
/// humidity, doorOpen and anomalies.
/// </summary>
public sealed record SensorPropertyDto(string Name, string Unit, IReadOnlyList<PropertyValueDto> Values);

/// <summary>
/// One time bucket. <see cref="Value"/> is the bucket average (for doorOpen, the fraction of
/// readings with the door open; for anomalies, the count). Min and Max are set only for
/// continuous measurements.
/// </summary>
public sealed record PropertyValueDto(DateTimeOffset Timestamp, double Value, double? Min = null, double? Max = null);
