using IoTDigitalTwin.Contracts.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using SensorDashboard.Api.Services;

namespace SensorDashboard.Api.Controllers;

[ApiController]
[Route("warehouses")]
public sealed class WarehousesController(DashboardService dashboard) : ControllerBase
{
    /// <summary>All warehouses with sensor, alert and offline counts.</summary>
    [HttpGet]
    [OutputCache(PolicyName = ResponseCaching.Live)]
    public async Task<IReadOnlyList<WarehouseDto>> GetAll(CancellationToken cancellationToken) =>
        await dashboard.GetWarehousesAsync(cancellationToken);

    /// <summary>
    /// Static 3D layout (floor and unit placements) for the digital-twin view. Changes only when
    /// metadata changes, so clients can cache it; live state comes from the sensors endpoint.
    /// </summary>
    [HttpGet("{id}/scene")]
    [OutputCache(PolicyName = ResponseCaching.Static)]
    [ResponseCache(Duration = 300)] // browsers may reuse the layout too
    [ProducesResponseType<WarehouseSceneDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WarehouseSceneDto>> GetScene(string id, CancellationToken cancellationToken) =>
        await dashboard.GetSceneAsync(id, cancellationToken) is { } scene
            ? Ok(scene)
            : Problem(statusCode: StatusCodes.Status404NotFound, title: $"Warehouse '{id}' not found.");

    /// <summary>Sensors in a warehouse with their current status and last reading.</summary>
    [HttpGet("{id}/sensors")]
    [OutputCache(PolicyName = ResponseCaching.Live)]
    [ProducesResponseType<IReadOnlyList<SensorDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<SensorDto>>> GetSensors(string id, CancellationToken cancellationToken) =>
        await dashboard.GetSensorsAsync(id, cancellationToken) is { } sensors
            ? Ok(sensors)
            : Problem(statusCode: StatusCodes.Status404NotFound, title: $"Warehouse '{id}' not found.");
}
