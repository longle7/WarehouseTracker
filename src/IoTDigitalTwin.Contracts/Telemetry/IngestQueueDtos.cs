namespace IoTDigitalTwin.Contracts.Telemetry;

/// <summary>
/// 202 response from POST /ingest in queued mode: the batch is durably stored and will be
/// written by the background consumer.
/// </summary>
public sealed record IngestAcceptedDto(long BatchId, int Received);

/// <summary>Health of the ingestion pipeline. Returned by GET /ingest/stats.</summary>
/// <param name="Mode">"queued" or "direct".</param>
/// <param name="QueueDepth">Batches waiting to be processed, including ones backing off after a failure.</param>
/// <param name="OldestQueuedAgeSeconds">Age of the oldest waiting batch; a growing value means the consumer is falling behind.</param>
/// <param name="Processed">Counters since the API started.</param>
public sealed record IngestStatsDto(
    string Mode,
    int QueueDepth,
    double? OldestQueuedAgeSeconds,
    int DeadLetterCount,
    IngestCountersDto Processed);

public sealed record IngestCountersDto(
    long Batches,
    long ReadingsInserted,
    long Duplicates,
    long Rejected,
    long FailedAttempts,
    long DeadLettered);

/// <summary>A batch that failed too many times. Returned by GET /ingest/dead-letters.</summary>
public sealed record DeadLetterDto(
    long Id,
    long BatchId,
    DateTimeOffset EnqueuedAt,
    DateTimeOffset DeadLetteredAt,
    int Attempts,
    int ReadingCount,
    string Error);
