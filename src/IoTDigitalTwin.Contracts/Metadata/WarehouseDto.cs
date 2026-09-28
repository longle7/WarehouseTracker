namespace IoTDigitalTwin.Contracts.Metadata;

/// <summary>
/// A warehouse with a live summary of its sensors. Returned by GET /warehouses.
/// </summary>
/// <param name="AlertCount">Sensors whose latest reading is flagged or outside their temperature range.</param>
/// <param name="OfflineCount">Active sensors with no recent reading.</param>
public sealed record WarehouseDto(
    string Id,
    string Name,
    string City,
    double Latitude,
    double Longitude,
    int SensorCount,
    int AlertCount,
    int OfflineCount);
