using System.Net.Http.Json;
using IoTDigitalTwin.Contracts.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SensorDashboard.Api.Data.Telemetry;
using static SensorDashboard.Api.Tests.Integration.TestData;

namespace SensorDashboard.Api.Tests.Integration;

[Collection(SqlServerCollection.Name)]
public class RollupTests(SqlServerFixture db) : IAsyncLifetime
{
    private readonly SqlReadingWriter _writer = new(db.ConnectionString);
    private readonly SqlReadingQueries _queries = new(db.ConnectionString);
    private readonly RollupService _rollups = new(
        db.ConnectionString, Options.Create(new TelemetryOptions()), TimeProvider.System, NullLogger<RollupService>.Instance);

    public Task InitializeAsync() => db.ResetTelemetryAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Rollup_buckets_match_aggregating_the_raw_readings()
    {
        var start = TelemetryStoreTests.MinuteAligned(-20);
        var random = new Random(11);
        var readings = Enumerable.Range(0, 180).Select(i => Reading(
            SeaDairy,
            start.AddSeconds(5 * i), // 15 minutes
            temperature: Math.Round(33 + random.NextDouble() * 10, 2),
            humidity: Math.Round(40 + random.NextDouble() * 20, 1),
            doorOpen: random.NextDouble() < 0.2,
            isAnomaly: random.NextDouble() < 0.1)).ToList();
        await _writer.WriteAsync(readings, default);
        await _rollups.RefreshAsync(default);

        foreach (var bucketMinutes in new[] { 1, 5 })
        {
            var bucket = TimeSpan.FromMinutes(bucketMinutes);
            var actual = await _queries.GetHistoryAsync(SeaDairy, start, start.AddMinutes(15), bucket, default);
            // Buckets align to the clock (DATE_BUCKET's midnight origin), not to the first reading.
            var expected = readings
                .GroupBy(r => new DateTimeOffset(r.Timestamp.UtcTicks - r.Timestamp.UtcTicks % bucket.Ticks, TimeSpan.Zero))
                .Where(g => g.Key >= start.AddTicks(-(start.UtcTicks % bucket.Ticks)))
                .OrderBy(g => g.Key)
                .ToList();

            Assert.Equal(expected.Count, actual.Count);
            foreach (var (e, a) in expected.Zip(actual))
            {
                Assert.Equal(e.Key, a.BucketStart);
                Assert.Equal(e.Count(), a.ReadingCount);
                Assert.Equal(e.Average(r => r.Temperature), a.AvgTemperature, precision: 6);
                Assert.Equal(e.Min(r => r.Temperature), a.MinTemperature);
                Assert.Equal(e.Max(r => r.Temperature), a.MaxTemperature);
                Assert.Equal(e.Average(r => r.Humidity), a.AvgHumidity, precision: 6);
                Assert.Equal(e.Count(r => r.DoorOpen) / (double)e.Count(), a.DoorOpenRatio, precision: 6);
                Assert.Equal(e.Count(r => r.IsAnomaly), a.AnomalyCount);
            }
        }
    }

    [Fact]
    public async Task Writer_marks_touched_minutes_dirty_and_refresh_clears_them()
    {
        var start = TelemetryStoreTests.MinuteAligned(-5);
        await _writer.WriteAsync([Reading(SeaDairy, start), Reading(SeaDairy, start.AddSeconds(30)), Reading(SeaDairy, start.AddMinutes(1))], default);

        Assert.Equal(2, await db.ScalarAsync<int>("SELECT COUNT(*) FROM telemetry.RollupDirty"));
        Assert.Equal(2, await _rollups.RefreshAsync(default));
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM telemetry.RollupDirty"));
    }

    [Fact]
    public async Task Late_readings_update_an_already_rolled_up_minute()
    {
        var start = TelemetryStoreTests.MinuteAligned(-30);
        await _writer.WriteAsync([Reading(SeaDairy, start, temperature: 36)], default);
        await _rollups.RefreshAsync(default);

        // Arrives after the minute was rolled up (e.g. replayed from the dead-letter queue).
        await _writer.WriteAsync([Reading(SeaDairy, start.AddSeconds(20), temperature: 40)], default);
        await _rollups.RefreshAsync(default);

        var bucket = Assert.Single(await _queries.GetHistoryAsync(SeaDairy, start, start.AddMinutes(1), TimeSpan.FromMinutes(1), default));
        Assert.Equal((2, 38.0, 40.0), (bucket.ReadingCount, bucket.AvgTemperature, bucket.MaxTemperature));
    }

    [Fact]
    public async Task Rollups_outlive_raw_readings()
    {
        var start = TelemetryStoreTests.MinuteAligned(-10);
        await _writer.WriteAsync([Reading(SeaDairy, start), Reading(SeaDairy, start.AddSeconds(10))], default);
        await _rollups.RefreshAsync(default);

        // Simulates raw-reading retention dropping the partition.
        await db.ExecuteAsync("TRUNCATE TABLE telemetry.SensorReadings;");

        var bucket = Assert.Single(await _queries.GetHistoryAsync(SeaDairy, start, start.AddMinutes(1), TimeSpan.FromMinutes(1), default));
        Assert.Equal(2, bucket.ReadingCount);
    }

    [Fact]
    public async Task Properties_endpoint_reports_which_store_served_it()
    {
        await using var factory = new ApiFactory(db.ConnectionString);
        var client = factory.CreateClient();

        var raw = await client.GetAsync($"/sensors/{SeaDairy}/properties?bucketSeconds=15");
        var rollup = await client.GetAsync($"/sensors/{SeaDairy}/properties?bucketSeconds=300");

        Assert.Equal("raw", raw.Headers.GetValues("X-History-Source").Single());
        Assert.Equal("rollup", rollup.Headers.GetValues("X-History-Source").Single());
        Assert.Equal(4, (await rollup.Content.ReadFromJsonAsync<List<SensorPropertyDto>>())!.Count);
    }
}
