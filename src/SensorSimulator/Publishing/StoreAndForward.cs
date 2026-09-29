using System.Net;
using IoTDigitalTwin.Contracts.Telemetry;
using Microsoft.Extensions.Options;
using SensorSimulator.Configuration;

namespace SensorSimulator.Publishing;

/// <summary>
/// Store-and-forward delivery, as an IoT edge device would do it. Sampling only
/// <see cref="Enqueue"/>s batches into a bounded buffer; this service's own loop delivers them
/// oldest-first, so a slow or unreachable API never stops readings from being taken, and
/// readings taken during an outage are sent once it recovers. Safe to resend because the API
/// stores readings idempotently.
/// </summary>
public sealed class StoreAndForward(
    IReadingPublisher publisher,
    IOptions<SimulatorOptions> options,
    TimeProvider timeProvider,
    ILogger<StoreAndForward> logger) : BackgroundService
{
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    private readonly LinkedList<PendingBatch> _backlog = new();
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _enqueued = new(0, int.MaxValue);
    private bool _failing;
    private long _dropped;

    /// <summary>Batches waiting for delivery.</summary>
    public int Backlog
    {
        get { lock (_gate) return _backlog.Count; }
    }

    /// <summary>Buffers a batch for delivery; never blocks. When full, the oldest batch goes.</summary>
    public void Enqueue(IReadOnlyList<SensorReadingDto> batch)
    {
        if (batch.Count == 0) return;

        lock (_gate)
        {
            _backlog.AddLast(new PendingBatch(batch, Guid.NewGuid().ToString("N")));
            while (_backlog.Count > options.Value.MaxBufferedBatches)
            {
                _backlog.RemoveFirst();
                if (_dropped++ % 100 == 0)
                {
                    logger.LogWarning("Buffer full ({Capacity} batches); dropping the oldest ({Dropped} dropped so far)",
                        options.Value.MaxBufferedBatches, _dropped);
                }
            }
        }
        _enqueued.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            if (await DeliverPendingAsync(stoppingToken))
            {
                failures = 0;
                await _enqueued.WaitAsync(stoppingToken);
            }
            else
            {
                // Exponential backoff while the API is unavailable: 1 s, 2 s, 4 s ... 30 s.
                failures++;
                var delay = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, failures - 1), MaxBackoff.TotalSeconds));
                await Task.Delay(delay, timeProvider, stoppingToken);
            }
        }
    }

    /// <summary>
    /// Delivers the backlog oldest-first. Batches the API permanently rejects are dropped so they
    /// can't block the rest.
    /// </summary>
    /// <returns>True once the backlog is empty; false if a transient failure stopped delivery.</returns>
    internal async Task<bool> DeliverPendingAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            LinkedListNode<PendingBatch>? next;
            lock (_gate) next = _backlog.First;
            if (next is null) break;

            try
            {
                await publisher.PublishAsync(next.Value.Readings, next.Value.IdempotencyKey, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException ex) when (IsPermanent(ex.StatusCode))
            {
                logger.LogError("API rejected a batch of {Count} readings ({Status}); dropping it", next.Value.Readings.Count, ex.StatusCode);
            }
            catch (Exception ex)
            {
                if (!_failing)
                {
                    logger.LogWarning("API unavailable ({Error}); buffering readings until it recovers", ex.Message);
                }
                _failing = true;
                return false;
            }

            // Remove what was sent (unless a full buffer already evicted it meanwhile).
            lock (_gate)
            {
                if (next.List is not null) _backlog.Remove(next);
            }
        }

        if (_failing)
        {
            logger.LogInformation("API reachable again; buffered readings delivered");
            _failing = false;
        }
        return true;
    }

    private sealed record PendingBatch(IReadOnlyList<SensorReadingDto> Readings, string IdempotencyKey);

    /// <summary>4xx other than 408 (timeout) and 429 (rate limited) won't succeed on retry.</summary>
    internal static bool IsPermanent(HttpStatusCode? status) =>
        status is { } s && (int)s is >= 400 and < 500 && s is not HttpStatusCode.RequestTimeout and not HttpStatusCode.TooManyRequests;
}
