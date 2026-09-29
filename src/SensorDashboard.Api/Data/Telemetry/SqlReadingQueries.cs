using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;

namespace SensorDashboard.Api.Data.Telemetry;

/// <summary>
/// Dapper queries against telemetry.SensorReadings. Every query filters on [Timestamp] so
/// SQL Server only touches the relevant daily partitions.
/// </summary>
public sealed class SqlReadingQueries(string connectionString) : IReadingQueries
{
    // One TOP (1) seek per sensor on UX_SensorReadings_SensorId_Timestamp. The CAST keeps the
    // predicate sargable against the varchar(32) key.
    private const string LatestSql = """
        SELECT r.SensorId, r.[Timestamp], r.Temperature, r.Humidity, r.DoorOpen, r.IsAnomaly
        FROM OPENJSON(@SensorIds) ids
        CROSS APPLY (
            SELECT TOP (1) SensorId, [Timestamp], Temperature, Humidity, DoorOpen, IsAnomaly
            FROM telemetry.SensorReadings
            WHERE SensorId = CAST(ids.value AS varchar(32)) AND [Timestamp] >= @Since
            ORDER BY [Timestamp] DESC
        ) r;
        """;

    // A streak is every reading after the sensor's last "good" one (in range / door closed)
    // within the lookback. MIN over an empty set is NULL, which means no streak. All lookups
    // seek the (SensorId, Timestamp) index.
    private const string StreaksSql = """
        SELECT t.SensorId,
               lr.LastReadingAt,
               ts.Since AS TemperatureSince, ts.StreakMin, ts.StreakMax,
               ds.Since AS DoorSince
        FROM OPENJSON(@Sensors) WITH (SensorId varchar(32), MinT decimal(5,2), MaxT decimal(5,2)) t
        OUTER APPLY (
            SELECT MAX(r.[Timestamp]) AS LastReadingAt
            FROM telemetry.SensorReadings r
            WHERE r.SensorId = t.SensorId AND r.[Timestamp] >= @Since
        ) lr
        OUTER APPLY (
            SELECT MIN(r.[Timestamp]) AS Since, MIN(r.Temperature) AS StreakMin, MAX(r.Temperature) AS StreakMax
            FROM telemetry.SensorReadings r
            WHERE r.SensorId = t.SensorId AND r.[Timestamp] >= @Since
              AND r.[Timestamp] > ISNULL((
                  SELECT MAX(g.[Timestamp]) FROM telemetry.SensorReadings g
                  WHERE g.SensorId = t.SensorId AND g.[Timestamp] >= @Since
                    AND g.Temperature BETWEEN t.MinT AND t.MaxT), '0001-01-01')
        ) ts
        OUTER APPLY (
            SELECT MIN(r.[Timestamp]) AS Since
            FROM telemetry.SensorReadings r
            WHERE r.SensorId = t.SensorId AND r.[Timestamp] >= @Since
              AND r.[Timestamp] > ISNULL((
                  SELECT MAX(g.[Timestamp]) FROM telemetry.SensorReadings g
                  WHERE g.SensorId = t.SensorId AND g.[Timestamp] >= @Since AND g.DoorOpen = 0), '0001-01-01')
        ) ds;
        """;

    private const string HistorySql = """
        SELECT b.BucketStart,
               COUNT(*)                          AS ReadingCount,
               AVG(CAST(r.Temperature AS float)) AS AvgTemperature,
               MIN(r.Temperature)                AS MinTemperature,
               MAX(r.Temperature)                AS MaxTemperature,
               AVG(CAST(r.Humidity AS float))    AS AvgHumidity,
               MIN(r.Humidity)                   AS MinHumidity,
               MAX(r.Humidity)                   AS MaxHumidity,
               AVG(CAST(r.DoorOpen AS float))    AS DoorOpenRatio,
               SUM(CAST(r.IsAnomaly AS int))     AS AnomalyCount,
               MAX(r.[Timestamp])                AS LastReadingAt
        FROM telemetry.SensorReadings r
        CROSS APPLY (SELECT DATE_BUCKET(second, @BucketSeconds, r.[Timestamp]) AS BucketStart) b
        WHERE r.SensorId = @SensorId AND r.[Timestamp] >= @From AND r.[Timestamp] < @To
        GROUP BY b.BucketStart
        ORDER BY b.BucketStart;
        """;

    // Same shape as HistorySql, from minute rollups. Minutes are included from the one
    // containing @From; bucket boundaries match the raw query's because both use DATE_BUCKET
    // with the same origin and the bucket is a whole number of minutes.
    private const string RollupHistorySql = """
        SELECT b.BucketStart,
               SUM(m.ReadingCount)                                        AS ReadingCount,
               CAST(SUM(m.SumTemperature) AS float) / SUM(m.ReadingCount) AS AvgTemperature,
               MIN(m.MinTemperature)                                      AS MinTemperature,
               MAX(m.MaxTemperature)                                      AS MaxTemperature,
               CAST(SUM(m.SumHumidity) AS float) / SUM(m.ReadingCount)    AS AvgHumidity,
               MIN(m.MinHumidity)                                         AS MinHumidity,
               MAX(m.MaxHumidity)                                         AS MaxHumidity,
               CAST(SUM(m.DoorOpenCount) AS float) / SUM(m.ReadingCount)  AS DoorOpenRatio,
               SUM(m.AnomalyCount)                                        AS AnomalyCount,
               -- Rollups are per minute, so the exact last reading isn't known here.
               CAST(NULL AS datetime2(3))                                 AS LastReadingAt
        FROM telemetry.SensorReadings1m m
        CROSS APPLY (SELECT DATE_BUCKET(second, @BucketSeconds, m.BucketStart) AS BucketStart) b
        WHERE m.SensorId = @SensorId
          AND m.BucketStart >= DATE_BUCKET(minute, 1, @From) AND m.BucketStart < @To
        GROUP BY b.BucketStart
        ORDER BY b.BucketStart;
        """;

    public async Task<IReadOnlyDictionary<string, LatestReading>> GetLatestAsync(
        IReadOnlyCollection<string> sensorIds, DateTimeOffset since, CancellationToken cancellationToken)
    {
        if (sensorIds.Count == 0)
        {
            return new Dictionary<string, LatestReading>();
        }

        var parameters = new DynamicParameters();
        parameters.Add("SensorIds", JsonSerializer.Serialize(sensorIds), DbType.String);
        parameters.Add("Since", since.UtcDateTime, DbType.DateTime2);

        await using var connection = new SqlConnection(connectionString);
        var rows = await connection.QueryAsync<LatestRow>(
            new CommandDefinition(LatestSql, parameters, cancellationToken: cancellationToken));

        return rows.ToDictionary(
            r => r.SensorId,
            r => new LatestReading(r.SensorId, AsUtc(r.Timestamp), (double)r.Temperature, (double)r.Humidity, r.DoorOpen, r.IsAnomaly));
    }

    public async Task<IReadOnlyDictionary<string, ConditionStreaks>> GetConditionStreaksAsync(
        IReadOnlyCollection<SensorThresholds> sensors, DateTimeOffset since, CancellationToken cancellationToken)
    {
        if (sensors.Count == 0)
        {
            return new Dictionary<string, ConditionStreaks>();
        }

        var parameters = new DynamicParameters();
        parameters.Add("Sensors", JsonSerializer.Serialize(sensors.Select(s => new { s.SensorId, MinT = s.MinTemperatureF, MaxT = s.MaxTemperatureF })), DbType.String);
        parameters.Add("Since", since.UtcDateTime, DbType.DateTime2);

        await using var connection = new SqlConnection(connectionString);
        var rows = await connection.QueryAsync<StreakRow>(
            new CommandDefinition(StreaksSql, parameters, cancellationToken: cancellationToken));

        return rows.ToDictionary(r => r.SensorId, r => new ConditionStreaks(
            r.LastReadingAt is { } last ? AsUtc(last) : null,
            r.TemperatureSince is { } ts ? AsUtc(ts) : null,
            (double?)r.StreakMin,
            (double?)r.StreakMax,
            r.DoorSince is { } ds ? AsUtc(ds) : null));
    }

    /// <summary>
    /// Buckets that are whole minutes are served from telemetry.SensorReadings1m (re-aggregating
    /// sums and counts), smaller ones from raw readings.
    /// </summary>
    public static bool UsesRollups(TimeSpan bucket) => bucket.TotalSeconds % 60 == 0;

    public async Task<IReadOnlyList<ReadingBucket>> GetHistoryAsync(
        string sensorId, DateTimeOffset from, DateTimeOffset to, TimeSpan bucket, CancellationToken cancellationToken)
    {
        var parameters = new DynamicParameters();
        parameters.Add("SensorId", sensorId, DbType.AnsiString, size: DigitalTwinDbContext.IdMaxLength);
        parameters.Add("From", from.UtcDateTime, DbType.DateTime2);
        parameters.Add("To", to.UtcDateTime, DbType.DateTime2);
        parameters.Add("BucketSeconds", (int)bucket.TotalSeconds, DbType.Int32);

        await using var connection = new SqlConnection(connectionString);
        var rows = await connection.QueryAsync<HistoryRow>(new CommandDefinition(
            UsesRollups(bucket) ? RollupHistorySql : HistorySql, parameters, cancellationToken: cancellationToken));

        return rows.Select(r => new ReadingBucket(
            AsUtc(r.BucketStart),
            r.ReadingCount,
            r.AvgTemperature,
            (double)r.MinTemperature,
            (double)r.MaxTemperature,
            r.AvgHumidity,
            (double)r.MinHumidity,
            (double)r.MaxHumidity,
            r.DoorOpenRatio,
            r.AnomalyCount,
            r.LastReadingAt is { } last ? AsUtc(last) : null)).ToList();
    }

    // Timestamps are stored as UTC datetime2; Dapper hands them back with Kind = Unspecified.
    private static DateTimeOffset AsUtc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed record LatestRow(
        string SensorId, DateTime Timestamp, decimal Temperature, decimal Humidity, bool DoorOpen, bool IsAnomaly);

    private sealed record StreakRow(
        string SensorId, DateTime? LastReadingAt, DateTime? TemperatureSince, decimal? StreakMin, decimal? StreakMax, DateTime? DoorSince);

    private sealed record HistoryRow(
        DateTime BucketStart,
        int ReadingCount,
        double AvgTemperature,
        decimal MinTemperature,
        decimal MaxTemperature,
        double AvgHumidity,
        decimal MinHumidity,
        decimal MaxHumidity,
        double DoorOpenRatio,
        int AnomalyCount,
        DateTime? LastReadingAt);
}
