using IoTDigitalTwin.Contracts.Telemetry;
using Microsoft.AspNetCore.Mvc;
using SensorDashboard.Api.Data.Telemetry;

namespace SensorDashboard.Api.Controllers;

/// <summary>
/// Telemetry ingestion (the API Gateway + Lambda side of the AWS design).
/// </summary>
[ApiController]
[Route("ingest")]
public sealed class IngestController(IReadingWriter writer) : ControllerBase
{
    public const int MaxBatchSize = 1000;

    [HttpPost]
    [ProducesResponseType<IngestResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IngestResultDto>> Post(
        [FromBody] IReadOnlyList<SensorReadingDto> readings,
        CancellationToken cancellationToken)
    {
        if (readings.Count is 0 or > MaxBatchSize)
        {
            ModelState.AddModelError(nameof(readings), $"A batch must contain between 1 and {MaxBatchSize} readings.");
            return ValidationProblem(ModelState);
        }

        return Ok(await writer.WriteAsync(readings, cancellationToken));
    }
}
