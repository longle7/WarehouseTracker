namespace SensorDashboard.Api.Data.Telemetry;

/// <summary>
/// Read path over the time-series store. Knows nothing about metadata: callers pass sensor
/// IDs in, so the store stays swappable independently of the EF Core metadata model.
/// </summary>
public interface IReadingQueries
{
    /// <summary>Latest reading at or after <paramref name="since"/> for each sensor that has one.</summary>
    Task<IReadOnlyDictionary<string, LatestReading>> GetLatestAsync(
        IReadOnlyCollection<string> sensorIds, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>Readings for one sensor in [from, to), aggregated into fixed-size time buckets.</summary>
    Task<IReadOnlyList<ReadingBucket>> GetHistoryAsync(
        string sensorId, DateTimeOffset from, DateTimeOffset to, TimeSpan bucket, CancellationToken cancellationToken);
}

public sealed record LatestReading(
    string SensorId,
    DateTimeOffset Timestamp,
    double Temperature,
    double Humidity,
    bool DoorOpen,
    bool IsAnomaly);

public sealed record ReadingBucket(
    DateTimeOffset BucketStart,
    int ReadingCount,
    double AvgTemperature,
    double MinTemperature,
    double MaxTemperature,
    double AvgHumidity,
    double MinHumidity,
    double MaxHumidity,
    double DoorOpenRatio,
    int AnomalyCount);
