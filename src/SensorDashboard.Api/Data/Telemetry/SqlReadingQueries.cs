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
               SUM(CAST(r.IsAnomaly AS int))     AS AnomalyCount
        FROM telemetry.SensorReadings r
        CROSS APPLY (SELECT DATE_BUCKET(second, @BucketSeconds, r.[Timestamp]) AS BucketStart) b
        WHERE r.SensorId = @SensorId AND r.[Timestamp] >= @From AND r.[Timestamp] < @To
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

    public async Task<IReadOnlyList<ReadingBucket>> GetHistoryAsync(
        string sensorId, DateTimeOffset from, DateTimeOffset to, TimeSpan bucket, CancellationToken cancellationToken)
    {
        var parameters = new DynamicParameters();
        parameters.Add("SensorId", sensorId, DbType.AnsiString, size: DigitalTwinDbContext.IdMaxLength);
        parameters.Add("From", from.UtcDateTime, DbType.DateTime2);
        parameters.Add("To", to.UtcDateTime, DbType.DateTime2);
        parameters.Add("BucketSeconds", (int)bucket.TotalSeconds, DbType.Int32);

        await using var connection = new SqlConnection(connectionString);
        var rows = await connection.QueryAsync<HistoryRow>(
            new CommandDefinition(HistorySql, parameters, cancellationToken: cancellationToken));

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
            r.AnomalyCount)).ToList();
    }

    // Timestamps are stored as UTC datetime2; Dapper hands them back with Kind = Unspecified.
    private static DateTimeOffset AsUtc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed record LatestRow(
        string SensorId, DateTime Timestamp, decimal Temperature, decimal Humidity, bool DoorOpen, bool IsAnomaly);

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
        int AnomalyCount);
}
