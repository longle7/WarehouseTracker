using IoTDigitalTwin.Contracts.Metadata;

namespace SensorDashboard.Api.Services;

/// <summary>
/// Physical size of each unit type in meters: width (door side), depth, height. Served with
/// the scene so the client renders any unit type generically from data.
/// </summary>
public static class UnitDimensions
{
    public static (double Width, double Depth, double Height) For(UnitType type) => type switch
    {
        UnitType.WalkInCooler => (6.0, 4.0, 3.2),
        UnitType.ReachInFridge => (1.4, 0.9, 2.1),
        UnitType.DisplayCase => (3.5, 1.2, 1.3),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}
