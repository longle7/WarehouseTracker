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

    /// <summary>
    /// For each sensor, when its current out-of-range and door-open streaks began (null if its
    /// latest reading is fine), plus its last reading time. Thresholds come from the caller.
    /// </summary>
    Task<IReadOnlyDictionary<string, ConditionStreaks>> GetConditionStreaksAsync(
        IReadOnlyCollection<SensorThresholds> sensors, DateTimeOffset since, CancellationToken cancellationToken);

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

public sealed record SensorThresholds(string SensorId, decimal MinTemperatureF, decimal MaxTemperatureF);

/// <param name="StreakMin">Lowest temperature during the out-of-range streak.</param>
/// <param name="StreakMax">Highest temperature during the out-of-range streak.</param>
public sealed record ConditionStreaks(
    DateTimeOffset? LastReadingAt,
    DateTimeOffset? TemperatureOutOfRangeSince,
    double? StreakMin,
    double? StreakMax,
    DateTimeOffset? DoorOpenSince)
{
    public static readonly ConditionStreaks None = new(null, null, null, null, null);
}

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
