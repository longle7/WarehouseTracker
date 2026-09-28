using SensorSimulator.Topology;

namespace SensorSimulator.Tests;

public class WarehouseTopologyTests
{
    [Fact]
    public void Each_warehouse_has_three_to_five_sensors_with_unique_ids()
    {
        Assert.All(WarehouseTopology.Seed, w =>
        {
            Assert.InRange(w.Sensors.Count, 3, 5);
            Assert.All(w.Sensors, s => Assert.Equal(w.Id, s.WarehouseId));
        });

        var ids = WarehouseTopology.Seed.SelectMany(w => w.Sensors).Select(s => s.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }
}
