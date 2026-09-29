using System.Net;
using System.Net.Http.Json;
using IoTDigitalTwin.Contracts;
using IoTDigitalTwin.Contracts.Metadata;
using IoTDigitalTwin.Contracts.Telemetry;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using static SensorDashboard.Api.Tests.Integration.TestData;

namespace SensorDashboard.Api.Tests.Integration;

/// <summary>End to end through the HTTP pipeline and the SignalR hub.</summary>
[Collection(SqlServerCollection.Name)]
public class ApiEndpointTests(SqlServerFixture db) : IAsyncLifetime
{
    private readonly ApiFactory _factory = new(db.ConnectionString);
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await db.ResetTelemetryAsync();
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Ingested_readings_show_up_in_sensor_status_and_warehouse_counts()
    {
        var now = Now();
        var ingest = await _client.PostAsJsonAsync("/ingest", new[]
        {
            Reading(SeaDairy, now, temperature: 37),
            Reading(SeaProduce, now, temperature: 44), // out of range
        });
        Assert.True(ingest.IsSuccessStatusCode, await ingest.Content.ReadAsStringAsync());

        var sensors = await Eventually.Until(
            () => _client.GetFromJsonAsync<List<SensorDto>>("/warehouses/WH-SEA/sensors", ContractJson.Options),
            s => s!.Single(x => x.Id == SeaProduce).Status == SensorStatus.Alert);
        Assert.Equal(SensorStatus.Ok, sensors!.Single(s => s.Id == SeaDairy).Status);
        Assert.Equal(44, sensors!.Single(s => s.Id == SeaProduce).LastReading!.Temperature);
        // Seeded but silent sensors are offline.
        Assert.All(sensors!.Where(s => s.Id is not SeaDairy and not SeaProduce), s => Assert.Equal(SensorStatus.Offline, s.Status));

        var warehouses = await _client.GetFromJsonAsync<List<WarehouseDto>>("/warehouses", ContractJson.Options);
        var seattle = warehouses!.Single(w => w.Id == Seattle);
        Assert.Equal((4, 1, 2), (seattle.SensorCount, seattle.AlertCount, seattle.OfflineCount));
    }

    [Fact]
    public async Task Invalid_batches_are_rejected_with_validation_problems()
    {
        var empty = await _client.PostAsJsonAsync("/ingest", Array.Empty<SensorReadingDto>());
        var badHumidity = await _client.PostAsJsonAsync("/ingest", new[] { Reading(SeaDairy, Now(), humidity: 150) });

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badHumidity.StatusCode);
        Assert.Contains("Humidity", await badHumidity.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Unknown_ids_return_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/warehouses/NOPE/sensors")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/warehouses/NOPE/scene")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/sensors/NOPE/properties")).StatusCode);
    }

    [Fact]
    public async Task Properties_returns_four_series_and_validates_the_range()
    {
        await _client.PostAsJsonAsync("/ingest", new[] { Reading(SeaDairy, Now()) });

        var properties = await Eventually.Until(
            () => _client.GetFromJsonAsync<List<SensorPropertyDto>>($"/sensors/{SeaDairy}/properties", ContractJson.Options),
            p => p!.Single(x => x.Name == "temperature").Values.Count > 0);
        var invalid = await _client.GetAsync($"/sensors/{SeaDairy}/properties?bucketSeconds=0");

        Assert.Equal(["temperature", "humidity", "doorOpen", "anomalies"], properties!.Select(p => p.Name));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Raw_history_carries_counts_and_the_metadata_clients_need_to_merge_pushes()
    {
        var now = Now();
        await _client.PostAsJsonAsync("/ingest", new[] { Reading(SeaDairy, now.AddSeconds(-2)), Reading(SeaDairy, now) });

        HttpResponseMessage response = null!;
        List<SensorPropertyDto>? properties = null;
        await Eventually.Until(async () =>
        {
            response = await _client.GetAsync($"/sensors/{SeaDairy}/properties?bucketSeconds=15");
            properties = await response.Content.ReadFromJsonAsync<List<SensorPropertyDto>>(ContractJson.Options);
            return properties!.Single(p => p.Name == "temperature").Values.Sum(v => v.Count);
        }, total => total == 2);

        Assert.Equal("15", response.Headers.GetValues("X-Bucket-Seconds").Single());
        Assert.Equal("raw", response.Headers.GetValues("X-History-Source").Single());
        Assert.Equal(now, DateTimeOffset.Parse(response.Headers.GetValues("X-Last-Reading-At").Single()));
        Assert.All(properties!, p => Assert.Equal(2, p.Values.Sum(v => v.Count)));
    }

    [Fact]
    public async Task Scene_describes_every_unit_with_dimensions()
    {
        var scene = await _client.GetFromJsonAsync<WarehouseSceneDto>("/warehouses/WH-SEA/scene", ContractJson.Options);

        Assert.Equal((40, 25), (scene!.FloorWidth, scene.FloorDepth));
        Assert.Equal(4, scene.Units.Count);
        Assert.All(scene.Units, u => Assert.True(u.Width > 0 && u.Depth > 0 && u.Height > 0));
    }

    [Fact]
    public async Task Hub_pushes_sensor_snapshots_to_warehouse_subscribers_after_ingest()
    {
        await using var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "/hubs/telemetry"), o =>
            {
                o.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                o.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
            })
            .AddJsonProtocol(o => o.PayloadSerializerOptions = ContractJson.Options)
            .Build();

        var pushed = new TaskCompletionSource<List<SensorDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<string, List<SensorDto>>("SensorsUpdated", (warehouseId, sensors) =>
        {
            var dairy = sensors.SingleOrDefault(s => s.Id == SeaDairy);
            if (warehouseId == Seattle && dairy?.LastReading?.Temperature == 39.5) pushed.TrySetResult(sensors);
        });
        await hub.StartAsync();
        await hub.InvokeAsync("SubscribeWarehouse", Seattle);

        await _client.PostAsJsonAsync("/ingest", new[] { Reading(SeaDairy, Now(), temperature: 39.5) });

        var sensors = await pushed.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(SensorStatus.Ok, sensors.Single(s => s.Id == SeaDairy).Status);
    }
}
