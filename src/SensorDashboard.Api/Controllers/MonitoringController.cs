using IoTDigitalTwin.Contracts.Monitoring;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using SensorDashboard.Api.Monitoring;
using SensorDashboard.Api.Services;

namespace SensorDashboard.Api.Controllers;

[ApiController]
public sealed class MonitoringController(StatusService status, ILogger<MonitoringController> logger) : ControllerBase
{
    /// <summary>Live health checks plus uptime (24 h, 7 d) and recent incidents.</summary>
    [HttpGet("status")]
    [OutputCache(PolicyName = ResponseCaching.Live)]
    public Task<SystemStatusDto> Status(CancellationToken cancellationToken) => status.GetAsync(cancellationToken);

    /// <summary>
    /// Records an unexpected dashboard error in the server log (rate-limited per client), so
    /// front-end failures are visible alongside API errors.
    /// </summary>
    [HttpPost("client-errors")]
    [EnableRateLimiting(ApiProtection.ClientErrorsPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult ClientError([FromBody] ClientErrorReportDto report)
    {
        logger.LogWarning("Client error in {Source} at {Url}: {Message}{NewLine}{Stack}",
            Truncate(report.Source, 50), Truncate(report.Url, 200), Truncate(report.Message, 500),
            Environment.NewLine, Truncate(report.Stack ?? "", 4000));
        return NoContent();
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
