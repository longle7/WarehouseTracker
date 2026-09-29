using IoTDigitalTwin.Contracts.Telemetry;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SensorDashboard.Api.Data.Ingestion;
using SensorDashboard.Api.Data.Telemetry;
using SensorDashboard.Api.Realtime;

namespace SensorDashboard.Api.Controllers;

/// <summary>
/// Telemetry ingestion (the API Gateway side of the AWS design). In queued mode it validates,
/// stores the batch durably and returns 202, leaving the write to QueuedIngestProcessor.
/// </summary>
[ApiController]
[Route("ingest")]
public sealed class IngestController(
    SqlReadingQueue queue,
    IngestMetrics metrics,
    IReadingWriter writer,
    ILiveUpdateNotifier liveUpdates,
    IOptions<IngestionOptions> options) : ControllerBase
{
    public const int MaxBatchSize = 1000;

    public const int MaxRequestBytes = 1024 * 1024;

    [HttpPost]
    [RequestSizeLimit(MaxRequestBytes)]
    [EnableRateLimiting(IngestRateLimiting.Policy)]
    [ProducesResponseType<IngestAcceptedDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<IngestResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Post([FromBody] IReadOnlyList<SensorReadingDto> readings, CancellationToken cancellationToken)
    {
        if (readings.Count is 0 or > MaxBatchSize)
        {
            ModelState.AddModelError(nameof(readings), $"A batch must contain between 1 and {MaxBatchSize} readings.");
            return ValidationProblem(ModelState);
        }

        if (options.Value.Mode == IngestionMode.Direct)
        {
            var result = await writer.WriteAsync(readings, cancellationToken);
            if (result.Inserted > 0) liveUpdates.NotifyIngested(readings);
            return Ok(result);
        }

        // Backpressure: shed load before the backlog grows without bound. Producers retry
        // after Retry-After (the simulator's Polly pipeline honors it).
        if (await queue.GetDepthAsync(cancellationToken) >= options.Value.MaxQueueDepth)
        {
            Response.Headers.RetryAfter = options.Value.RetryAfterSeconds.ToString();
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Ingestion backlog is full; retry later.");
        }

        var batchId = await queue.EnqueueAsync(readings, cancellationToken);
        // Location points at the batch's status, so producers can see what happened to it.
        return AcceptedAtAction(nameof(Batch), new { id = batchId }, new IngestAcceptedDto(batchId, readings.Count));
    }

    /// <summary>What happened to a queued batch: queued, retrying, processed (with counts) or dead-lettered.</summary>
    [HttpGet("batches/{id:long}")]
    [ProducesResponseType<IngestBatchStatusDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IngestBatchStatusDto>> Batch(long id, CancellationToken cancellationToken) =>
        await queue.GetBatchStatusAsync(id, cancellationToken) is { } status
            ? Ok(status)
            : Problem(statusCode: StatusCodes.Status404NotFound, title: $"Batch {id} not found (unknown, replayed, or past retention).");

    /// <summary>Queue depth, backlog age, dead letters and processing counters.</summary>
    [HttpGet("stats")]
    public async Task<IngestStatsDto> Stats(CancellationToken cancellationToken)
    {
        var (depth, oldest, deadLetters) = await queue.GetStatsAsync(cancellationToken);
        return new IngestStatsDto(
            options.Value.Mode.ToString().ToLowerInvariant(), depth, oldest, deadLetters, metrics.Snapshot());
    }

    /// <summary>Most recent dead-lettered batches (payloads omitted).</summary>
    [HttpGet("dead-letters")]
    public Task<IReadOnlyList<DeadLetterDto>> DeadLetters([FromQuery] int limit = 50, CancellationToken cancellationToken = default) =>
        queue.ListDeadLettersAsync(Math.Clamp(limit, 1, 500), cancellationToken);

    /// <summary>Puts a dead-lettered batch back on the queue with a fresh attempt budget.</summary>
    [HttpPost("dead-letters/{id:long}/replay")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Replay(long id, CancellationToken cancellationToken) =>
        await queue.ReplayDeadLetterAsync(id, cancellationToken) is { } batchId
            ? Accepted(new { batchId })
            : Problem(statusCode: StatusCodes.Status404NotFound, title: $"Dead letter {id} not found.");
}
