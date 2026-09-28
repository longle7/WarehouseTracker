namespace SensorDashboard.Api.Data.Ingestion;

public enum IngestionMode
{
    /// <summary>POST /ingest enqueues and returns 202; a background consumer writes (API Gateway → SQS → Lambda).</summary>
    Queued,

    /// <summary>POST /ingest writes synchronously and returns the result.</summary>
    Direct,
}

public sealed class IngestionOptions
{
    public const string SectionName = "Ingestion";

    public IngestionMode Mode { get; set; } = IngestionMode.Queued;

    /// <summary>Backpressure: at or above this many waiting batches, POST /ingest returns 503.</summary>
    public int MaxQueueDepth { get; set; } = 10_000;

    /// <summary>Retry-After (seconds) sent with the 503, which the simulator's Polly retry honors.</summary>
    public int RetryAfterSeconds { get; set; } = 5;

    /// <summary>Attempts before a batch moves to the dead-letter table.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Upper bound on the exponential backoff between attempts.</summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How often the consumer checks the queue when idle. Enqueues on this instance wake it
    /// immediately; polling covers batches enqueued by other instances and retries coming due.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);
}
