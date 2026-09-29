using System.Net;
using System.Net.Http.Json;
using IoTDigitalTwin.Contracts;
using IoTDigitalTwin.Contracts.Telemetry;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using SensorDashboard.Api.Data.Ingestion;
using static SensorDashboard.Api.Tests.Integration.TestData;

namespace SensorDashboard.Api.Tests.Integration;

[Collection(SqlServerCollection.Name)]
public class IngestQueueTests(SqlServerFixture db) : IAsyncLifetime
{
    private readonly SqlReadingQueue _queue = new(
        db.Connections, Options.Create(new IngestionOptions { MaxAttempts = 3 }), TimeProvider.System);

    public async Task InitializeAsync()
    {
        await db.ResetTelemetryAsync();
        await db.ExecuteAsync("DELETE ingest.ReadingBatches; DELETE ingest.DeadLetters; DELETE ingest.BatchResults;");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Post_enqueues_returns_202_and_the_consumer_stores_the_readings()
    {
        await using var factory = new ApiFactory(db.ConnectionString);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/ingest", new[] { Reading(SeaDairy, Now()), Reading(SeaProduce, Now()) });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var accepted = await response.Content.ReadFromJsonAsync<IngestAcceptedDto>();
        Assert.Equal(2, accepted!.Received);
        await Eventually.Until(async () => await db.ScalarAsync<int>("SELECT COUNT(*) FROM telemetry.SensorReadings") == 2);
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM ingest.ReadingBatches"));

        var stats = await client.GetFromJsonAsync<IngestStatsDto>("/ingest/stats");
        Assert.Equal("queued", stats!.Mode);
        Assert.Equal(2, stats.Processed.ReadingsInserted);
    }

    [Fact]
    public async Task Full_backlog_returns_503_with_retry_after()
    {
        await using var factory = new ApiFactory(db.ConnectionString).WithWebHostBuilder(b =>
            b.UseSetting("Ingestion:MaxQueueDepth", "0"));
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/ingest", new[] { Reading(SeaDairy, Now()) });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("5", response.Headers.RetryAfter?.ToString());
    }

    [Fact]
    public async Task Clients_over_the_rate_limit_get_429_with_retry_after()
    {
        await using var factory = new ApiFactory(db.ConnectionString).WithWebHostBuilder(b =>
        {
            b.UseSetting("Ingestion:RateLimitPerSecond", "1");
            b.UseSetting("Ingestion:RateLimitBurst", "2");
        });
        var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            statuses.Add((await client.PostAsJsonAsync("/ingest", new[] { Reading(SeaDairy, Now().AddSeconds(i)) })).StatusCode);
        }
        var limited = await client.PostAsJsonAsync("/ingest", new[] { Reading(SeaDairy, Now().AddSeconds(9)) });

        Assert.Equal([HttpStatusCode.Accepted, HttpStatusCode.Accepted], statuses.Take(2));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(int.Parse(limited.Headers.GetValues("Retry-After").Single()) >= 1);
    }


    [Fact]
    public async Task Retried_requests_with_the_same_idempotency_key_are_queued_once()
    {
        await using var factory = new ApiFactory(db.ConnectionString);
        var client = factory.CreateClient();
        var body = new[] { Reading(SeaDairy, Now()) };

        async Task<HttpResponseMessage> Send(string key)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/ingest") { Content = JsonContent.Create(body) };
            request.Headers.Add("Idempotency-Key", key);
            return await client.SendAsync(request);
        }

        var first = await Send("batch-123");
        var retry = await Send("batch-123");
        var other = await Send("batch-456");

        var firstId = (await first.Content.ReadFromJsonAsync<IngestAcceptedDto>())!.BatchId;
        Assert.Equal(firstId, (await retry.Content.ReadFromJsonAsync<IngestAcceptedDto>())!.BatchId);
        Assert.NotEqual(firstId, (await other.Content.ReadFromJsonAsync<IngestAcceptedDto>())!.BatchId);
        Assert.False(first.Headers.Contains("Idempotency-Replayed"));
        Assert.Equal("true", retry.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal(1, await db.ScalarAsync<int>("SELECT COUNT(*) FROM ingest.IdempotencyKeys WHERE [Key] = N'batch-123'"));
    }

    [Fact]
    public async Task Overlong_idempotency_keys_are_rejected()
    {
        await using var factory = new ApiFactory(db.ConnectionString);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/ingest") { Content = JsonContent.Create(new[] { Reading(SeaDairy, Now()) }) };
        request.Headers.Add("Idempotency-Key", new string('k', 101));

        Assert.Equal(HttpStatusCode.BadRequest, (await factory.CreateClient().SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Dequeue_is_undone_by_rollback()
    {
        await _queue.EnqueueAsync([Reading(SeaDairy, Now())], null, default);

        await using (var connection = new SqlConnection(db.ConnectionString))
        {
            await connection.OpenAsync();
            await using var tx = (SqlTransaction)await connection.BeginTransactionAsync();
            Assert.NotNull(await SqlReadingQueue.DequeueAsync(connection, tx, default));
            await tx.RollbackAsync();
        }

        Assert.Equal(1, await db.ScalarAsync<int>("SELECT COUNT(*) FROM ingest.ReadingBatches"));
    }

    [Fact]
    public async Task Freshly_enqueued_batches_are_immediately_due()
    {
        // Regression: a millisecond-precision AvailableAt rounded SYSUTCDATETIME() into the
        // future about half the time, hiding brand-new batches from an immediate dequeue.
        await using var connection = new SqlConnection(db.ConnectionString);
        await connection.OpenAsync();
        for (var i = 0; i < 200; i++)
        {
            var id = (await _queue.EnqueueAsync([Reading(SeaDairy, Now())], null, default)).BatchId;
            await using var command = new SqlCommand(
                $"SELECT COUNT(*) FROM ingest.ReadingBatches WHERE Id = {id} AND AvailableAt <= SYSUTCDATETIME();", connection);
            Assert.Equal(1, (int)(await command.ExecuteScalarAsync())!);
        }
    }

    [Fact]
    public async Task Concurrent_consumers_never_take_the_same_batch()
    {
        await _queue.EnqueueAsync([Reading(SeaDairy, Now())], null, default);
        await _queue.EnqueueAsync([Reading(SeaProduce, Now())], null, default);

        await using var c1 = new SqlConnection(db.ConnectionString);
        await using var c2 = new SqlConnection(db.ConnectionString);
        await c1.OpenAsync();
        await c2.OpenAsync();
        await using var t1 = (SqlTransaction)await c1.BeginTransactionAsync();
        await using var t2 = (SqlTransaction)await c2.BeginTransactionAsync();

        // The first consumer holds its batch locked; READPAST makes the second skip it.
        var first = await SqlReadingQueue.DequeueAsync(c1, t1, default);
        var second = await SqlReadingQueue.DequeueAsync(c2, t2, default);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first!.Id, second!.Id);
    }

    [Fact]
    public async Task Failures_back_off_then_dead_letter_and_can_be_replayed()
    {
        var id = (await _queue.EnqueueAsync([Reading(SeaDairy, Now())], null, default)).BatchId;
        var batch = new QueuedBatch(id, Attempts: 0, Payload: "", EnqueuedAt: default);

        // Attempts 1 and 2 of 3: rescheduled into the future.
        Assert.False(await _queue.RecordFailureAsync(batch, "boom", poison: false, default));
        Assert.Equal(1, await db.ScalarAsync<int>($"SELECT Attempts FROM ingest.ReadingBatches WHERE Id = {id}"));
        Assert.Equal(1, await db.ScalarAsync<int>($"SELECT COUNT(*) FROM ingest.ReadingBatches WHERE Id = {id} AND AvailableAt > SYSUTCDATETIME()"));
        Assert.False(await _queue.RecordFailureAsync(batch with { Attempts = 1 }, "boom", poison: false, default));

        // Attempt 3 of 3: moved to dead letters.
        Assert.True(await _queue.RecordFailureAsync(batch with { Attempts = 2 }, "boom", poison: false, default));
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM ingest.ReadingBatches"));
        var deadLetter = Assert.Single(await _queue.ListDeadLettersAsync(10, default));
        Assert.Equal((id, 3, "boom"), (deadLetter.BatchId, deadLetter.Attempts, deadLetter.Error));

        // Replay puts it back with a fresh budget.
        Assert.NotNull(await _queue.ReplayDeadLetterAsync(deadLetter.Id, default));
        Assert.Empty(await _queue.ListDeadLettersAsync(10, default));
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT Attempts FROM ingest.ReadingBatches"));
    }

    [Fact]
    public async Task Poison_batch_is_dead_lettered_on_first_failure_by_the_consumer()
    {
        await db.ExecuteAsync("INSERT ingest.ReadingBatches (ReadingCount, Payload) VALUES (1, N'this is not json');");
        await using var factory = new ApiFactory(db.ConnectionString);
        var client = factory.CreateClient();

        await Eventually.Until(async () => await db.ScalarAsync<int>("SELECT COUNT(*) FROM ingest.DeadLetters") == 1);

        var deadLetter = Assert.Single((await client.GetFromJsonAsync<List<DeadLetterDto>>("/ingest/dead-letters"))!);
        Assert.Equal(1, deadLetter.Attempts);
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM ingest.ReadingBatches"));
    }

    [Fact]
    public async Task Accepted_response_links_to_a_status_that_reports_the_outcome()
    {
        await using var factory = new ApiFactory(db.ConnectionString);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/ingest", new[]
        {
            Reading(SeaDairy, Now()),
            Reading(SeaProduce, Now()),
            Reading("WH-XXX-FRG-99", Now(), warehouseId: "WH-XXX"),
        });
        var accepted = await response.Content.ReadFromJsonAsync<IngestAcceptedDto>();

        Assert.Equal($"/ingest/batches/{accepted!.BatchId}", response.Headers.Location!.AbsolutePath);
        IngestBatchStatusDto? status = null;
        await Eventually.Until(async () =>
        {
            status = await client.GetFromJsonAsync<IngestBatchStatusDto>(response.Headers.Location, ContractJson.Options);
            return status!.Status == IngestBatchStatus.Processed;
        });
        Assert.Equal(new IngestResultDto(Received: 3, Inserted: 2, Duplicates: 0, Rejected: 1), status!.Result);
        Assert.Equal(1, status.Attempts);
        Assert.NotNull(status.CompletedAt);
    }

    [Fact]
    public async Task Status_reports_queued_retrying_and_dead_lettered_batches()
    {
        var queued = (await _queue.EnqueueAsync([Reading(SeaDairy, Now())], null, default)).BatchId;
        Assert.Equal(IngestBatchStatus.Queued, (await _queue.GetBatchStatusAsync(queued, default))!.Status);

        await _queue.RecordFailureAsync(new QueuedBatch(queued, 0, "", default), "db timeout", poison: false, default);
        var retrying = (await _queue.GetBatchStatusAsync(queued, default))!;
        Assert.Equal((IngestBatchStatus.Retrying, 1, "db timeout"), (retrying.Status, retrying.Attempts, retrying.LastError));
        Assert.True(retrying.NextAttemptAt > DateTimeOffset.UtcNow);

        await _queue.RecordFailureAsync(new QueuedBatch(queued, 1, "", default), "bad payload", poison: true, default);
        var dead = (await _queue.GetBatchStatusAsync(queued, default))!;
        Assert.Equal((IngestBatchStatus.DeadLettered, "bad payload"), (dead.Status, dead.LastError));
        Assert.Null(dead.Result);

        Assert.Null(await _queue.GetBatchStatusAsync(987654321, default));
    }

    [Fact]
    public async Task Unknown_batch_returns_404()
    {
        await using var factory = new ApiFactory(db.ConnectionString);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClient().GetAsync("/ingest/batches/987654321")).StatusCode);
    }

}
