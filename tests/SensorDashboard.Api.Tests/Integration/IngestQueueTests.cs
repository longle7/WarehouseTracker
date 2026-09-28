using System.Net;
using System.Net.Http.Json;
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
        db.ConnectionString, Options.Create(new IngestionOptions { MaxAttempts = 3 }), TimeProvider.System);

    public async Task InitializeAsync()
    {
        await db.ResetTelemetryAsync();
        await db.ExecuteAsync("DELETE ingest.ReadingBatches; DELETE ingest.DeadLetters;");
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
        await WaitUntilAsync(async () => await db.ScalarAsync<int>("SELECT COUNT(*) FROM telemetry.SensorReadings") == 2);
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
    public async Task Dequeue_is_undone_by_rollback()
    {
        await _queue.EnqueueAsync([Reading(SeaDairy, Now())], default);

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
    public async Task Concurrent_consumers_never_take_the_same_batch()
    {
        await _queue.EnqueueAsync([Reading(SeaDairy, Now())], default);
        await _queue.EnqueueAsync([Reading(SeaProduce, Now())], default);

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
        var id = await _queue.EnqueueAsync([Reading(SeaDairy, Now())], default);
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

        await WaitUntilAsync(async () => await db.ScalarAsync<int>("SELECT COUNT(*) FROM ingest.DeadLetters") == 1);

        var deadLetter = Assert.Single((await client.GetFromJsonAsync<List<DeadLetterDto>>("/ingest/dead-letters"))!);
        Assert.Equal(1, deadLetter.Attempts);
        Assert.Equal(0, await db.ScalarAsync<int>("SELECT COUNT(*) FROM ingest.ReadingBatches"));
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, int timeoutSeconds = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition not met in time.");
            await Task.Delay(100);
        }
    }
}
