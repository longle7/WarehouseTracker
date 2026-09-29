using System.Text.Json;
using IoTDigitalTwin.Contracts.Telemetry;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using SensorDashboard.Api.Data.Telemetry;
using SensorDashboard.Api.Realtime;

namespace SensorDashboard.Api.Data.Ingestion;

/// <summary>Pipeline counters since startup, for GET /ingest/stats.</summary>
public sealed class IngestMetrics
{
    private long _batches, _inserted, _duplicates, _rejected, _failed, _deadLettered;

    public void Processed(IngestResultDto result)
    {
        Interlocked.Increment(ref _batches);
        Interlocked.Add(ref _inserted, result.Inserted);
        Interlocked.Add(ref _duplicates, result.Duplicates);
        Interlocked.Add(ref _rejected, result.Rejected);
    }

    public void Failed(bool deadLettered)
    {
        Interlocked.Increment(ref _failed);
        if (deadLettered) Interlocked.Increment(ref _deadLettered);
    }

    public IngestCountersDto Snapshot() => new(
        Interlocked.Read(ref _batches),
        Interlocked.Read(ref _inserted),
        Interlocked.Read(ref _duplicates),
        Interlocked.Read(ref _rejected),
        Interlocked.Read(ref _failed),
        Interlocked.Read(ref _deadLettered));
}

/// <summary>
/// Queue consumer (the SQS-triggered Lambda of the AWS design). Each batch is dequeued and
/// written in one transaction: commit removes it, any failure rolls back and it is retried
/// with backoff, then dead-lettered. Writes are idempotent, so even an ambiguous commit that
/// leads to reprocessing can't duplicate readings. Safe to run on several instances at once.
/// </summary>
public sealed class QueuedIngestProcessor(
    SqlConnectionFactory connections,
    SqlReadingQueue queue,
    IngestMetrics metrics,
    ILiveUpdateNotifier liveUpdates,
    IOptions<IngestionOptions> options,
    ILogger<QueuedIngestProcessor> logger) : BackgroundService
{
    private static readonly TimeSpan TrimInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan MaxOutageBackoff = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextTrim = DateTime.UtcNow;
        var consecutiveFailures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Drain everything that's due, then sleep until woken or the poll interval.
                while (await ProcessNextAsync(stoppingToken)) { }
                if (DateTime.UtcNow >= nextTrim)
                {
                    await queue.TrimBatchResultsAsync(stoppingToken);
                    nextTrim = DateTime.UtcNow + TrimInterval;
                }
                if (consecutiveFailures > 0)
                {
                    logger.LogInformation("Ingest queue reachable again after {Failures} failed attempts", consecutiveFailures);
                    consecutiveFailures = 0;
                }
                await queue.WaitForWorkAsync(options.Value.PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Database unreachable or similar: nothing was dequeued, so back off
                // exponentially (capped) rather than hammering a database that's down.
                consecutiveFailures++;
                var delay = TimeSpan.FromTicks(Math.Min(
                    options.Value.PollInterval.Ticks * (1L << Math.Min(consecutiveFailures, 10)),
                    MaxOutageBackoff.Ticks));
                logger.Log(consecutiveFailures == 1 ? LogLevel.Warning : LogLevel.Debug, ex,
                    "Ingest queue unavailable (attempt {Attempt}); retrying in {Delay}", consecutiveFailures, delay);
                await Task.Delay(delay, stoppingToken);
            }
        }
    }

    /// <returns>True if a batch was taken (processed or failed), false if none was due.</returns>
    internal async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var batch = await SqlReadingQueue.DequeueAsync(connection, transaction, cancellationToken);
        if (batch is null)
        {
            return false;
        }

        IReadOnlyList<SensorReadingDto> readings;
        IngestResultDto result;
        try
        {
            readings = SqlReadingQueue.Deserialize(batch);
            result = await SqlReadingWriter.WriteAsync(connection, transaction, readings, cancellationToken);
            await SqlReadingQueue.RecordResultAsync(connection, transaction, batch, result, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            var poison = ex is JsonException;
            var deadLettered = await queue.RecordFailureAsync(batch, ex.Message, poison, CancellationToken.None);
            metrics.Failed(deadLettered);
            logger.Log(
                deadLettered ? LogLevel.Error : LogLevel.Warning,
                ex,
                "Ingest batch {BatchId} failed (attempt {Attempt}); {Outcome}",
                batch.Id, batch.Attempts + 1, deadLettered ? "moved to dead letters" : "will retry");
            return true;
        }

        metrics.Processed(result);
        if (result.Rejected > 0)
        {
            logger.LogWarning("Ingest batch {BatchId}: {Rejected} of {Received} readings rejected (unknown or inactive sensor)",
                batch.Id, result.Rejected, result.Received);
        }
        if (result.Inserted > 0)
        {
            liveUpdates.NotifyIngested(readings);
        }
        return true;
    }
}
