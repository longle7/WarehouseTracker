using IoTDigitalTwin.Contracts.Telemetry;

namespace SensorDashboard.Api.Data.Telemetry;

/// <summary>
/// Ingestion write path (the Timestream side of the AWS design). Kept separate from the
/// read-side queries so each can be tuned or swapped independently.
/// </summary>
public interface IReadingWriter
{
    /// <summary>
    /// Stores a batch idempotently: a reading already stored for the same sensor and
    /// timestamp is counted as a duplicate rather than failing the batch.
    /// </summary>
    Task<IngestResultDto> WriteAsync(IReadOnlyList<SensorReadingDto> readings, CancellationToken cancellationToken);
}
