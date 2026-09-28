namespace IoTDigitalTwin.Contracts.Metadata;

/// <summary>
/// Static 3D layout of a warehouse, in the spirit of an IoT TwinMaker scene: a floor plus one
/// entity per monitored unit. Live state is not included; clients join it by sensor ID with
/// the sensor list (GET /warehouses/{id}/sensors or the SensorsUpdated push).
/// Coordinates are meters: X runs along the floor width, Z along its depth, origin at a corner.
/// </summary>
public sealed record WarehouseSceneDto(
    string WarehouseId,
    double FloorWidth,
    double FloorDepth,
    IReadOnlyList<SceneUnitDto> Units);

/// <param name="X">Center of the unit's footprint along the floor width.</param>
/// <param name="Z">Center of the unit's footprint along the floor depth.</param>
/// <param name="RotationDegrees">Turn about the vertical axis; 0 means the door faces +Z.</param>
/// <param name="Width">Size along the unit's local X axis (door side), in meters.</param>
public sealed record SceneUnitDto(
    string SensorId,
    string Location,
    UnitType UnitType,
    double X,
    double Z,
    double RotationDegrees,
    double Width,
    double Depth,
    double Height);

/// <summary>Kind of refrigerated unit a sensor is mounted in. Serialized camelCase.</summary>
public enum UnitType
{
    WalkInCooler,
    ReachInFridge,
    DisplayCase,
}
