using System.Data;
using System.Text.Json;
using Dapper;
using IoTDigitalTwin.Contracts;
using IoTDigitalTwin.Contracts.Telemetry;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace SensorDashboard.Api.Data.Ingestion;

/// <summary>
/// A durable work queue in SQL Server (the SQS of the AWS design). Consumers dequeue with
/// READPAST so several can run at once without blocking or double-processing, and the
/// dequeue shares a transaction with the reading insert, so a batch is removed from the
/// queue if and only if its readings are stored.
/// </summary>
public sealed class SqlReadingQueue(SqlConnectionFactory connections, IOptions<IngestionOptions> options, TimeProvider timeProvider)
{
    // Wakes the local consumer as soon as something is enqueued on this instance.
    private readonly SemaphoreSlim _enqueued = new(0, int.MaxValue);

    public async Task<long> EnqueueAsync(IReadOnlyList<SensorReadingDto> readings, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        var id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT ingest.ReadingBatches (ReadingCount, Payload)
            OUTPUT inserted.Id
            VALUES (@Count, @Payload);
            """,
            new { Count = readings.Count, Payload = JsonSerializer.Serialize(readings, ContractJson.Options) },
            cancellationToken: cancellationToken));

        _enqueued.Release();
        return id;
    }

    public Task WaitForWorkAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _enqueued.WaitAsync(timeout, cancellationToken);

    public async Task<int> GetDepthAsync(CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM ingest.ReadingBatches;", cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Takes the next due batch inside <paramref name="transaction"/>. It is deleted only if the
    /// transaction commits; a rollback puts it straight back.
    /// </summary>
    public static async Task<QueuedBatch?> DequeueAsync(
        SqlConnection connection, SqlTransaction transaction, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<QueuedRow>(new CommandDefinition(
            """
            WITH next AS (
                SELECT TOP (1) Id, Attempts, Payload, EnqueuedAt
                FROM ingest.ReadingBatches WITH (ROWLOCK, READPAST, UPDLOCK)
                WHERE AvailableAt <= SYSUTCDATETIME()
                ORDER BY Id
            )
            DELETE FROM next
            OUTPUT deleted.Id, deleted.Attempts, deleted.Payload, deleted.EnqueuedAt;
            """,
            transaction: transaction,
            cancellationToken: cancellationToken));

        return row is null ? null : new QueuedBatch(row.Id, row.Attempts, row.Payload, row.EnqueuedAt.AsUtc());
    }

    /// <summary>Records a processed batch's outcome inside the processing transaction.</summary>
    public static Task RecordResultAsync(
        SqlConnection connection, SqlTransaction transaction, QueuedBatch batch, IngestResultDto result, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT ingest.BatchResults (BatchId, EnqueuedAt, Attempts, Received, Inserted, Duplicates, Rejected)
            VALUES (@Id, @EnqueuedAt, @Attempts, @Received, @Inserted, @Duplicates, @Rejected);
            """,
            new
            {
                batch.Id,
                EnqueuedAt = batch.EnqueuedAt.UtcDateTime,
                Attempts = batch.Attempts + 1,
                result.Received,
                result.Inserted,
                result.Duplicates,
                result.Rejected,
            },
            transaction: transaction,
            cancellationToken: cancellationToken));

    /// <summary>
    /// Where a batch is now: still queued or retrying, processed, or dead-lettered. A batch being
    /// processed right now is row-locked, so this waits the few milliseconds until it commits
    /// rather than reporting a misleading state.
    /// </summary>
    /// <returns>Null if the ID is unknown (or its result has aged out).</returns>
    public async Task<IngestBatchStatusDto?> GetBatchStatusAsync(long id, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        var row = await connection.QuerySingleOrDefaultAsync<BatchStatusRow>(new CommandDefinition(
            """
            SELECT 'processed' AS Status, Attempts, Received, Inserted, Duplicates, Rejected,
                   CAST(NULL AS nvarchar(2000)) AS LastError, CAST(NULL AS datetime2(3)) AS NextAttemptAt, ProcessedAt AS CompletedAt
            FROM ingest.BatchResults WHERE BatchId = @Id
            UNION ALL
            SELECT CASE WHEN Attempts = 0 THEN 'queued' ELSE 'retrying' END, Attempts, ReadingCount, NULL, NULL, NULL,
                   LastError, AvailableAt, NULL
            FROM ingest.ReadingBatches WHERE Id = @Id
            UNION ALL
            SELECT 'deadLettered', Attempts, ReadingCount, NULL, NULL, NULL, Error, NULL, DeadLetteredAt
            FROM ingest.DeadLetters WHERE BatchId = @Id;
            """,
            new { Id = id },
            cancellationToken: cancellationToken));

        if (row is null) return null;

        var status = row.Status switch
        {
            "processed" => IngestBatchStatus.Processed,
            "queued" => IngestBatchStatus.Queued,
            "retrying" => IngestBatchStatus.Retrying,
            _ => IngestBatchStatus.DeadLettered,
        };
        var result = row.Inserted is { } inserted
            ? new IngestResultDto(row.Received, inserted, row.Duplicates!.Value, row.Rejected!.Value)
            : null;
        return new IngestBatchStatusDto(
            id,
            status,
            row.Attempts,
            row.Received,
            result,
            row.LastError,
            status == IngestBatchStatus.Retrying && row.NextAttemptAt is { } next ? next.AsUtc() : null,
            row.CompletedAt is { } done ? done.AsUtc() : null);
    }

    /// <summary>Deletes processed-batch results past retention, a chunk at a time.</summary>
    public async Task<int> TrimBatchResultsAsync(CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE TOP (10000) FROM ingest.BatchResults WHERE ProcessedAt < @Cutoff;",
            new { Cutoff = (timeProvider.GetUtcNow() - options.Value.BatchResultRetention).UtcDateTime },
            cancellationToken: cancellationToken));
    }

    public static IReadOnlyList<SensorReadingDto> Deserialize(QueuedBatch batch) =>
        JsonSerializer.Deserialize<List<SensorReadingDto>>(batch.Payload, ContractJson.Options)
        ?? throw new JsonException("Batch payload is null.");

    /// <summary>
    /// Records a failed attempt after the processing transaction rolled back: reschedules the
    /// batch with exponential backoff, or dead-letters it once it has used all its attempts
    /// (immediately when <paramref name="poison"/>, e.g. unreadable JSON that no retry will fix).
    /// </summary>
    /// <returns>True if the batch was dead-lettered.</returns>
    public async Task<bool> RecordFailureAsync(QueuedBatch batch, string error, bool poison, CancellationToken cancellationToken)
    {
        var attempts = batch.Attempts + 1;
        var deadLetter = poison || attempts >= options.Value.MaxAttempts;
        error = error.Length > 2000 ? error[..2000] : error;

        await using var connection = connections.Create();
        if (deadLetter)
        {
            var moved = await connection.ExecuteAsync(new CommandDefinition(
                """
                SET XACT_ABORT ON;
                BEGIN TRAN;
                INSERT ingest.DeadLetters (BatchId, EnqueuedAt, Attempts, ReadingCount, Payload, Error)
                SELECT Id, EnqueuedAt, @Attempts, ReadingCount, Payload, @Error
                FROM ingest.ReadingBatches WITH (UPDLOCK) WHERE Id = @Id;
                DELETE ingest.ReadingBatches WHERE Id = @Id;
                COMMIT;
                """,
                new { batch.Id, Attempts = attempts, Error = error },
                cancellationToken: cancellationToken));
            return moved > 0;
        }

        // 2^attempts seconds with ±20% jitter so a burst of failures doesn't retry in lockstep.
        var backoff = TimeSpan.FromSeconds(Math.Pow(2, attempts) * (0.8 + Random.Shared.NextDouble() * 0.4));
        if (backoff > options.Value.MaxBackoff) backoff = options.Value.MaxBackoff;

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE ingest.ReadingBatches
            SET Attempts = @Attempts, LastError = @Error, AvailableAt = @AvailableAt
            WHERE Id = @Id;
            """,
            new { batch.Id, Attempts = attempts, Error = error, AvailableAt = (timeProvider.GetUtcNow() + backoff).UtcDateTime },
            cancellationToken: cancellationToken));
        return false;
    }

    public async Task<(int Depth, double? OldestAgeSeconds, int DeadLetters)> GetStatsAsync(CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        var row = await connection.QuerySingleAsync<(int Depth, DateTime? Oldest, int DeadLetters)>(new CommandDefinition(
            """
            SELECT (SELECT COUNT(*) FROM ingest.ReadingBatches),
                   (SELECT MIN(EnqueuedAt) FROM ingest.ReadingBatches),
                   (SELECT COUNT(*) FROM ingest.DeadLetters);
            """,
            cancellationToken: cancellationToken));
        double? age = row.Oldest is { } oldest ? (timeProvider.GetUtcNow() - oldest.AsUtc()).TotalSeconds : null;
        return (row.Depth, age, row.DeadLetters);
    }

    public async Task<IReadOnlyList<DeadLetterDto>> ListDeadLettersAsync(int limit, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        var rows = await connection.QueryAsync<DeadLetterRow>(new CommandDefinition(
            """
            SELECT TOP (@Limit) Id, BatchId, EnqueuedAt, DeadLetteredAt, Attempts, ReadingCount, Error
            FROM ingest.DeadLetters ORDER BY Id DESC;
            """,
            new { Limit = limit },
            cancellationToken: cancellationToken));
        return rows.Select(r => new DeadLetterDto(
            r.Id, r.BatchId, r.EnqueuedAt.AsUtc(), r.DeadLetteredAt.AsUtc(), r.Attempts, r.ReadingCount, r.Error)).ToList();
    }

    /// <summary>Moves a dead letter back onto the queue with a fresh attempt budget.</summary>
    /// <returns>The new batch ID, or null if no such dead letter exists.</returns>
    public async Task<long?> ReplayDeadLetterAsync(long id, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        var batchId = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
            """
            SET XACT_ABORT ON;
            BEGIN TRAN;
            DECLARE @new TABLE (Id bigint);
            INSERT ingest.ReadingBatches (ReadingCount, Payload)
            OUTPUT inserted.Id INTO @new
            SELECT ReadingCount, Payload FROM ingest.DeadLetters WITH (UPDLOCK) WHERE Id = @Id;
            DELETE ingest.DeadLetters WHERE Id = @Id;
            COMMIT;
            SELECT Id FROM @new;
            """,
            new { Id = id },
            cancellationToken: cancellationToken));

        if (batchId is not null) _enqueued.Release();
        return batchId;
    }


    private sealed record QueuedRow(long Id, int Attempts, string Payload, DateTime EnqueuedAt);

    private sealed record BatchStatusRow(
        string Status, int Attempts, int Received, int? Inserted, int? Duplicates, int? Rejected,
        string? LastError, DateTime? NextAttemptAt, DateTime? CompletedAt);

    private sealed record DeadLetterRow(
        long Id, long BatchId, DateTime EnqueuedAt, DateTime DeadLetteredAt, int Attempts, int ReadingCount, string Error);
}

public sealed record QueuedBatch(long Id, int Attempts, string Payload, DateTimeOffset EnqueuedAt);
