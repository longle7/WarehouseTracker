using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using IoTDigitalTwin.Contracts.Metadata;
using SensorSimulator.Publishing;

namespace SensorSimulator.Topology;

/// <summary>Where the simulator gets the warehouses and sensors to simulate.</summary>
public interface ITopologySource
{
    Task<IReadOnlyList<Warehouse>> LoadAsync(CancellationToken cancellationToken);
}

public enum TopologySourceKind
{
    /// <summary>Load from SensorDashboard.Api, so the simulator always matches the metadata.</summary>
    Api,

    /// <summary>Use the built-in seed list (offline use, no API needed to start).</summary>
    Seed,
}

/// <summary>
/// Reads the topology from the API's GET /warehouses and /warehouses/{id}/sensors, keeping only
/// active sensors: the API is the single source of truth, so a sensor added or disabled there
/// is picked up on the simulator's next start.
/// </summary>
public sealed class ApiTopologySource(IHttpClientFactory httpClientFactory) : ITopologySource
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public async Task<IReadOnlyList<Warehouse>> LoadAsync(CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpReadingPublisher.HttpClientName);
        var warehouses = await client.GetFromJsonAsync<List<WarehouseDto>>("/warehouses", Json, cancellationToken)
            ?? throw new InvalidOperationException("The API returned no warehouses.");

        var result = new List<Warehouse>(warehouses.Count);
        foreach (var warehouse in warehouses)
        {
            var sensors = await client.GetFromJsonAsync<List<SensorDto>>(
                $"/warehouses/{Uri.EscapeDataString(warehouse.Id)}/sensors", Json, cancellationToken) ?? [];
            var active = sensors
                .Where(s => s.IsActive)
                .Select(s => new Sensor(s.Id, s.WarehouseId, s.Location))
                .ToList();
            if (active.Count > 0)
            {
                result.Add(new Warehouse(warehouse.Id, warehouse.Name, active));
            }
        }
        return result;
    }
}

public sealed class SeedTopologySource : ITopologySource
{
    public Task<IReadOnlyList<Warehouse>> LoadAsync(CancellationToken cancellationToken) =>
        Task.FromResult(WarehouseTopology.Seed);
}
