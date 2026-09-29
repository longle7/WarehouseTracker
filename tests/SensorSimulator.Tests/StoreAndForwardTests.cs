using System.Net;
using IoTDigitalTwin.Contracts.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SensorSimulator.Configuration;
using SensorSimulator.Publishing;

namespace SensorSimulator.Tests;

public class StoreAndForwardTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    private static IReadOnlyList<SensorReadingDto> Batch(int n) =>
    [
        new SensorReadingDto
        {
            SensorId = "S1", WarehouseId = "W1", Timestamp = T0.AddSeconds(n), Temperature = 37, Humidity = 50, DoorOpen = false,
        },
    ];

    private static (StoreAndForward Delivery, FakePublisher Api) Create(int capacity = 720)
    {
        var api = new FakePublisher();
        var options = Options.Create(new SimulatorOptions { MaxBufferedBatches = capacity });
        return (new StoreAndForward(api, options, TimeProvider.System, NullLogger<StoreAndForward>.Instance), api);
    }

    private static int Tick(IReadOnlyList<SensorReadingDto> batch) => (int)(batch[0].Timestamp - T0).TotalSeconds;

    [Fact]
    public async Task Healthy_api_receives_batches_in_order()
    {
        var (delivery, api) = Create();
        delivery.Enqueue(Batch(1));
        delivery.Enqueue(Batch(2));

        Assert.True(await delivery.DeliverPendingAsync(default));

        Assert.Equal([1, 2], api.Delivered.Select(Tick));
        Assert.Equal(0, delivery.Backlog);
    }

    [Fact]
    public async Task Readings_keep_being_buffered_during_an_outage_and_are_delivered_in_order_after_it()
    {
        var (delivery, api) = Create();

        api.Down = true;
        for (var i = 1; i <= 3; i++)
        {
            delivery.Enqueue(Batch(i)); // sampling never blocks on the network
            Assert.False(await delivery.DeliverPendingAsync(default));
        }
        Assert.Empty(api.Delivered);
        Assert.Equal(3, delivery.Backlog);

        api.Down = false;
        delivery.Enqueue(Batch(4));
        Assert.True(await delivery.DeliverPendingAsync(default));

        Assert.Equal([1, 2, 3, 4], api.Delivered.Select(Tick));
        Assert.Equal(0, delivery.Backlog);
    }

    [Fact]
    public async Task Full_buffer_drops_the_oldest_batches()
    {
        var (delivery, api) = Create(capacity: 3);
        for (var i = 1; i <= 5; i++) delivery.Enqueue(Batch(i));

        await delivery.DeliverPendingAsync(default);

        Assert.Equal([3, 4, 5], api.Delivered.Select(Tick));
    }

    [Fact]
    public async Task Permanently_rejected_batches_are_dropped_without_blocking_the_rest()
    {
        var (delivery, api) = Create();
        api.RejectTick = 2;
        for (var i = 1; i <= 3; i++) delivery.Enqueue(Batch(i));

        Assert.True(await delivery.DeliverPendingAsync(default));

        Assert.Equal([1, 3], api.Delivered.Select(Tick));
        Assert.Equal(0, delivery.Backlog);
    }

    [Fact]
    public void Empty_batches_are_ignored()
    {
        var (delivery, _) = Create();
        delivery.Enqueue([]);
        Assert.Equal(0, delivery.Backlog);
    }

    [Fact]
    public async Task Background_loop_delivers_what_sampling_enqueues()
    {
        var (delivery, api) = Create();
        await delivery.StartAsync(default);
        try
        {
            delivery.Enqueue(Batch(1));
            delivery.Enqueue(Batch(2));

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (api.Delivered.Count < 2 && DateTime.UtcNow < deadline) await Task.Delay(20);

            Assert.Equal([1, 2], api.Delivered.Select(Tick));
        }
        finally
        {
            await delivery.StopAsync(default);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, true)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, true)]
    [InlineData(HttpStatusCode.RequestTimeout, false)]
    [InlineData(HttpStatusCode.TooManyRequests, false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false)]
    [InlineData(null, false)] // connection refused, circuit open
    public void Only_non_retryable_client_errors_are_permanent(HttpStatusCode? status, bool permanent) =>
        Assert.Equal(permanent, StoreAndForward.IsPermanent(status));

    private sealed class FakePublisher : IReadingPublisher
    {
        private readonly Lock _gate = new();
        private readonly List<IReadOnlyList<SensorReadingDto>> _delivered = [];

        public bool Down { get; set; }
        public int? RejectTick { get; set; }

        public IReadOnlyList<IReadOnlyList<SensorReadingDto>> Delivered
        {
            get { lock (_gate) return [.. _delivered]; }
        }

        public Task PublishAsync(IReadOnlyList<SensorReadingDto> readings, CancellationToken cancellationToken)
        {
            if (Down) throw new HttpRequestException("Connection refused");
            if (Tick(readings) == RejectTick) throw new HttpRequestException("Bad batch", null, HttpStatusCode.BadRequest);
            lock (_gate) _delivered.Add(readings);
            return Task.CompletedTask;
        }
    }
}
