namespace IoTDigitalTwin.Contracts.Telemetry;

/// <summary>
/// Response from POST /ingest.
/// </summary>
/// <param name="Received">Readings in the request.</param>
/// <param name="Inserted">Readings newly stored.</param>
/// <param name="Duplicates">Readings already stored for the same sensor and timestamp (e.g. a retried batch).</param>
/// <param name="Rejected">Readings from unknown or inactive sensors, or whose warehouse doesn't match the sensor's.</param>
public sealed record IngestResultDto(int Received, int Inserted, int Duplicates, int Rejected);
