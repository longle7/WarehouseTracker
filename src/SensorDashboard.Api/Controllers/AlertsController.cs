using IoTDigitalTwin.Contracts.Alerts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.SignalR;
using SensorDashboard.Api.Data.Alerts;
using SensorDashboard.Api.Realtime;
using SensorDashboard.Api.Services;

namespace SensorDashboard.Api.Controllers;

[ApiController]
[Route("alerts")]
public sealed class AlertsController(
    AlertService alerts,
    IHubContext<TelemetryHub, ITelemetryClient> hub,
    IOutputCacheStore outputCache) : ControllerBase
{
    /// <summary>Alerts, active (open or acknowledged) by default; most severe and newest first.</summary>
    [HttpGet]
    [OutputCache(PolicyName = ResponseCaching.Alerts)]
    public Task<IReadOnlyList<AlertDto>> List(
        [FromQuery] AlertFilter state = AlertFilter.Active,
        [FromQuery] string? warehouseId = null,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default) =>
        alerts.ListAsync(state, warehouseId, Math.Clamp(limit, 1, 500), cancellationToken);

    /// <summary>Marks an active alert as being handled. Idempotent; resolved alerts return 409.</summary>
    [HttpPost("{id:long}/acknowledge")]
    [ProducesResponseType<AlertDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AlertDto>> Acknowledge(
        long id, [FromBody] AcknowledgeAlertDto? body, CancellationToken cancellationToken)
    {
        var (outcome, alert) = await alerts.AcknowledgeAsync(id, body?.By, cancellationToken);
        switch (outcome)
        {
            case AcknowledgeOutcome.NotFound:
                return Problem(statusCode: StatusCodes.Status404NotFound, title: $"Alert {id} not found.");
            case AcknowledgeOutcome.AlreadyResolved:
                return Problem(statusCode: StatusCodes.Status409Conflict, title: $"Alert {id} is already resolved.");
            case AcknowledgeOutcome.Acknowledged:
                // Cached alert lists are now stale; drop them before telling dashboards to refetch.
                await outputCache.EvictByTagAsync(ResponseCaching.AlertsTag, cancellationToken);
                await hub.Clients.All.AlertsChanged([alert!]);
                break;
        }
        return Ok(alert);
    }
}
