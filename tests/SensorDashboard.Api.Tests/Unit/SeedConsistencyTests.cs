using SensorDashboard.Api.Data;
using SensorDashboard.Api.Services;
using SensorSimulator.Topology;

namespace SensorDashboard.Api.Tests.Unit;

/// <summary>
/// The simulator and the API each carry the seed topology. If they drift, ingestion silently
/// rejects readings as coming from unknown sensors, so pin them together here.
/// </summary>
public class SeedConsistencyTests
{
    [Fact]
    public void Simulator_topology_matches_api_seed_data()
    {
        var simulator = WarehouseTopology.Seed
            .SelectMany(w => w.Sensors)
            .Select(s => (s.Id, s.WarehouseId, s.Location))
            .OrderBy(s => s.Id);
        var api = MetadataSeed.Sensors
            .Select(s => (s.Id, s.WarehouseId, s.Location))
            .OrderBy(s => s.Id);

        Assert.Equal(simulator, api);
    }

    [Fact]
    public void Every_unit_fits_inside_its_warehouse_floor()
    {
        var floors = MetadataSeed.Warehouses.ToDictionary(w => w.Id);
        Assert.All(MetadataSeed.Sensors, s =>
        {
            var floor = floors[s.WarehouseId];
            var (width, depth, _) = UnitDimensions.For(s.UnitType);
            var turned = Math.Round(s.RotationDegrees / 90) % 2 != 0;
            var (halfX, halfZ) = turned ? (depth / 2, width / 2) : (width / 2, depth / 2);

            Assert.InRange(s.PositionX - halfX, 0, floor.FloorWidthM);
            Assert.InRange(s.PositionX + halfX, 0, floor.FloorWidthM);
            Assert.InRange(s.PositionZ - halfZ, 0, floor.FloorDepthM);
            Assert.InRange(s.PositionZ + halfZ, 0, floor.FloorDepthM);
        });
    }
}
