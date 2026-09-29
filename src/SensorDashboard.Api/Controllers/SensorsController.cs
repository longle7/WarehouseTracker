using IoTDigitalTwin.Contracts.Telemetry;
using Microsoft.AspNetCore.Mvc;
using SensorDashboard.Api.Data.Telemetry;
using SensorDashboard.Api.Services;

namespace SensorDashboard.Api.Controllers;

[ApiController]
[Route("sensors")]
public sealed class SensorsController(DashboardService dashboard) : ControllerBase
{
    /// <summary>
    /// Time-series history for one sensor, bucketed. Defaults to the last hour; the bucket
    /// size is chosen automatically (about 300 points) unless <paramref name="bucketSeconds"/> is given.
    /// </summary>
    [HttpGet("{sensorId}/properties")]
    [ProducesResponseType<IReadOnlyList<SensorPropertyDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<SensorPropertyDto>>> GetProperties(
        string sensorId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int? bucketSeconds,
        CancellationToken cancellationToken)
    {
        var (range, error) = dashboard.ResolveRange(from, to, bucketSeconds);
        if (range is null)
        {
            ModelState.AddModelError("range", error!);
            return ValidationProblem(ModelState);
        }

        if (await dashboard.GetPropertiesAsync(sensorId, range, cancellationToken) is not { } history)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: $"Sensor '{sensorId}' not found.");
        }

        // Metadata that lets clients fold pushed readings into this response exactly:
        // the bucket size, which store served it ("raw" or "rollup"), and the newest reading
        // it already includes (raw only), so a pushed reading is never counted twice.
        Response.Headers["X-Bucket-Seconds"] = ((int)range.Bucket.TotalSeconds).ToString();
        Response.Headers["X-History-Source"] = SqlReadingQueries.UsesRollups(range.Bucket) ? "rollup" : "raw";
        if (history.LastReadingAt is { } last)
        {
            Response.Headers["X-Last-Reading-At"] = last.ToString("O");
        }
        return Ok(history.Properties);
    }
}
