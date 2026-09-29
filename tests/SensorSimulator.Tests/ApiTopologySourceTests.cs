using System.Net;
using System.Text;
using SensorSimulator.Topology;

namespace SensorSimulator.Tests;

public class ApiTopologySourceTests
{
    [Fact]
    public async Task Loads_warehouses_with_their_active_sensors_from_the_api()
    {
        var source = new ApiTopologySource(new StubHttpClientFactory(new Dictionary<string, string>
        {
            ["/warehouses"] = """
                [{ "id": "WH-A", "name": "Alpha", "city": "X", "latitude": 0, "longitude": 0, "sensorCount": 2, "alertCount": 0, "offlineCount": 0 },
                 { "id": "WH-B", "name": "Beta",  "city": "Y", "latitude": 0, "longitude": 0, "sensorCount": 1, "alertCount": 0, "offlineCount": 0 }]
                """,
            ["/warehouses/WH-A/sensors"] = """
                [{ "id": "A-1", "warehouseId": "WH-A", "location": "Cooler", "minTemperatureF": 34, "maxTemperatureF": 40, "isActive": true,  "status": "ok",       "lastReading": null },
                 { "id": "A-2", "warehouseId": "WH-A", "location": "Spare",  "minTemperatureF": 34, "maxTemperatureF": 40, "isActive": false, "status": "inactive", "lastReading": null }]
                """,
            // Every sensor disabled: the warehouse is skipped entirely.
            ["/warehouses/WH-B/sensors"] = """
                [{ "id": "B-1", "warehouseId": "WH-B", "location": "Old", "minTemperatureF": 34, "maxTemperatureF": 40, "isActive": false, "status": "inactive", "lastReading": null }]
                """,
        }));

        var warehouses = await source.LoadAsync(default);

        var warehouse = Assert.Single(warehouses);
        Assert.Equal(("WH-A", "Alpha"), (warehouse.Id, warehouse.Name));
        Assert.Equal(new Sensor("A-1", "WH-A", "Cooler"), Assert.Single(warehouse.Sensors));
    }

    [Fact]
    public async Task Api_errors_surface_so_the_worker_can_retry()
    {
        var source = new ApiTopologySource(new StubHttpClientFactory(new Dictionary<string, string>()));
        await Assert.ThrowsAsync<HttpRequestException>(() => source.LoadAsync(default));
    }

    private sealed class StubHttpClientFactory(Dictionary<string, string> responses) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(new StubHandler(responses)) { BaseAddress = new Uri("http://api.test") };
    }

    private sealed class StubHandler(Dictionary<string, string> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responses.TryGetValue(request.RequestUri!.AbsolutePath, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}
