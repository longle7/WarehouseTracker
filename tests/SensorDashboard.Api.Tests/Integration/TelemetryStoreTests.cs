using Microsoft.EntityFrameworkCore;
using SensorDashboard.Api.Data.Telemetry;
using static SensorDashboard.Api.Tests.Integration.TestData;

namespace SensorDashboard.Api.Tests.Integration;

/// <summary>Writer, queries and partition maintenance against the real partitioned schema.</summary>
[Collection(SqlServerCollection.Name)]
public class TelemetryStoreTests(SqlServerFixture db) : IAsyncLifetime
{
    private readonly SqlReadingWriter _writer = new(db.Connections);
    private readonly SqlReadingQueries _queries = new(db.Connections);

    public Task InitializeAsync() => db.ResetTelemetryAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Writes_valid_readings()
    {
        var now = Now();
        var result = await _writer.WriteAsync([Reading(SeaDairy, now), Reading(SeaProduce, now)], default);

        Assert.Equal(new(Received: 2, Inserted: 2, Duplicates: 0, Rejected: 0), result);
        Assert.Equal(2, await db.ScalarAsync<int>("SELECT COUNT(*) FROM telemetry.SensorReadings"));
    }

    [Fact]
    public async Task Replayed_batch_is_counted_as_duplicates_not_stored_twice()
    {
        var batch = new[] { Reading(SeaDairy, Now()), Reading(SeaProduce, Now()) };

        await _writer.WriteAsync(batch, default);
        var replay = await _writer.WriteAsync(batch, default);

        Assert.Equal(new(Received: 2, Inserted: 0, Duplicates: 2, Rejected: 0), replay);
        Assert.Equal(2, await db.ScalarAsync<int>("SELECT COUNT(*) FROM telemetry.SensorReadings"));
    }

    [Fact]
    public async Task Duplicates_within_one_batch_are_stored_once()
    {
        var reading = Reading(SeaDairy, Now());

        var result = await _writer.WriteAsync([reading, reading], default);

        Assert.Equal(1, result.Inserted);
        Assert.Equal(1, result.Duplicates);
    }

    [Fact]
    public async Task Rejects_unknown_sensors_and_mismatched_warehouses()
    {
        var now = Now();
        var result = await _writer.WriteAsync(
        [
            Reading(SeaDairy, now),
            Reading("WH-XXX-FRG-99", now, warehouseId: "WH-XXX"),
            Reading(SeaProduce, now, warehouseId: "WH-PDX"),
        ], default);

        Assert.Equal(new(Received: 3, Inserted: 1, Duplicates: 0, Rejected: 2), result);
    }

    [Fact]
    public async Task Rejects_readings_from_inactive_sensors()
    {
        await using var context = db.CreateDbContext();
        await context.Sensors.Where(s => s.Id == SeaDairy).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        try
        {
            var result = await _writer.WriteAsync([Reading(SeaDairy, Now())], default);
            Assert.Equal(1, result.Rejected);
        }
        finally
        {
            await context.Sensors.Where(s => s.Id == SeaDairy).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, true));
        }
    }

    [Fact]
    public async Task Latest_returns_the_newest_reading_per_sensor_within_the_window()
    {
        var now = Now();
        await _writer.WriteAsync(
        [
            Reading(SeaDairy, now.AddSeconds(-10), temperature: 36),
            Reading(SeaDairy, now, temperature: 38.5, doorOpen: true),
            Reading(SeaProduce, now.AddHours(-2), temperature: 35),
        ], default);

        var latest = await _queries.GetLatestAsync([SeaDairy, SeaProduce], now.AddHours(-1), default);

        var dairy = Assert.Single(latest).Value;
        Assert.Equal(SeaDairy, dairy.SensorId);
        Assert.Equal(now, dairy.Timestamp);
        Assert.Equal(38.5, dairy.Temperature);
        Assert.True(dairy.DoorOpen);
    }

    [Fact]
    public async Task History_aggregates_raw_readings_into_sub_minute_buckets()
    {
        var start = MinuteAligned(-10);
        await _writer.WriteAsync(Enumerable.Range(0, 12).Select(i => Reading(
            SeaDairy, start.AddSeconds(5 * i), temperature: i < 6 ? 36 : 38, doorOpen: i == 0)).ToList(), default);

        var buckets = await _queries.GetHistoryAsync(SeaDairy, start, start.AddMinutes(1), TimeSpan.FromSeconds(30), default);

        Assert.Equal(2, buckets.Count);
        Assert.Equal((6, 36.0, 1 / 6.0), (buckets[0].ReadingCount, buckets[0].AvgTemperature, buckets[0].DoorOpenRatio));
        Assert.Equal((6, 38.0, 0.0), (buckets[1].ReadingCount, buckets[1].AvgTemperature, buckets[1].DoorOpenRatio));
    }

    [Fact]
    public async Task History_aggregates_readings_into_minute_buckets_via_rollups()
    {
        // Two full minutes: 12 readings each, 5 s apart.
        var start = MinuteAligned(-10);
        var readings = Enumerable.Range(0, 24).Select(i => Reading(
            SeaDairy,
            start.AddSeconds(5 * i),
            temperature: i < 12 ? 36 + (i % 2) : 45, // minute 1 alternates 36/37; minute 2 is an excursion
            doorOpen: i is 0 or 1 or 2,
            isAnomaly: i >= 12)).ToList();
        await _writer.WriteAsync(readings, default);
        await db.CreateRollupService().RefreshAsync(default);

        var buckets = await _queries.GetHistoryAsync(SeaDairy, start, start.AddMinutes(2), TimeSpan.FromMinutes(1), default);

        Assert.Equal(2, buckets.Count);
        var first = buckets[0];
        Assert.Equal(start, first.BucketStart);
        Assert.Equal(12, first.ReadingCount);
        Assert.Equal(36.5, first.AvgTemperature, precision: 6);
        Assert.Equal(36, first.MinTemperature);
        Assert.Equal(37, first.MaxTemperature);
        Assert.Equal(3 / 12.0, first.DoorOpenRatio, precision: 6);
        Assert.Equal(0, first.AnomalyCount);
        Assert.Equal(12, buckets[1].AnomalyCount);
        Assert.Equal(45, buckets[1].AvgTemperature, precision: 6);
    }

    internal static DateTimeOffset MinuteAligned(int minutesFromNow)
    {
        var now = DateTimeOffset.UtcNow;
        return new DateTimeOffset(now.Ticks - now.Ticks % TimeSpan.TicksPerMinute, TimeSpan.Zero).AddMinutes(minutesFromNow);
    }


    [Fact]
    public async Task Partition_maintenance_keeps_a_week_of_future_partitions_and_is_idempotent()
    {
        await db.ExecuteAsync("EXEC telemetry.usp_MaintainReadingPartitions @DaysAhead = 7, @RetentionDays = 30;");
        var boundaries = await db.ScalarAsync<int>(
            """
            SELECT COUNT(*) FROM sys.partition_range_values rv
            JOIN sys.partition_functions pf ON pf.function_id = rv.function_id
            WHERE pf.name = N'pf_ReadingsDaily' AND CAST(rv.value AS date) >= CAST(SYSUTCDATETIME() AS date)
            """);
        await db.ExecuteAsync("EXEC telemetry.usp_MaintainReadingPartitions @DaysAhead = 7, @RetentionDays = 30;");
        var again = await db.ScalarAsync<int>("SELECT fanout FROM sys.partition_functions WHERE name = N'pf_ReadingsDaily'");

        Assert.Equal(8, boundaries); // today + 7 days
        Assert.Equal(again, await db.ScalarAsync<int>("SELECT fanout FROM sys.partition_functions WHERE name = N'pf_ReadingsDaily'"));
    }
}
