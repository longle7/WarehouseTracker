using System.ComponentModel.DataAnnotations;

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
    [Range(0, 10_000_000)]
    public int MaxQueueDepth { get; set; } = 10_000;

    /// <summary>Retry-After (seconds) sent with the 503, which the simulator's Polly retry honors.</summary>
    [Range(1, 3600)]
    public int RetryAfterSeconds { get; set; } = 5;

    /// <summary>Attempts before a batch moves to the dead-letter table.</summary>
    [Range(1, 100)]
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Upper bound on the exponential backoff between attempts.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How often the consumer checks the queue when idle. Enqueues on this instance wake it
    /// immediately; polling covers batches enqueued by other instances and retries coming due.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00.100", "00:01:00")]
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Sustained POST /ingest requests per second allowed per client (token bucket refill).</summary>
    [Range(1, 10_000)]
    public int RateLimitPerSecond { get; set; } = 20;

    /// <summary>Burst of POST /ingest requests a client may make above the sustained rate.</summary>
    [Range(1, 10_000)]
    public int RateLimitBurst { get; set; } = 50;

    /// <summary>How long processed-batch results stay queryable via GET /ingest/batches/{id}.</summary>
    [Range(typeof(TimeSpan), "00:01:00", "365.00:00:00")]
    public TimeSpan BatchResultRetention { get; set; } = TimeSpan.FromDays(7);
}
