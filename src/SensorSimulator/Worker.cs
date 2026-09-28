using Microsoft.Extensions.Options;
using SensorSimulator.Configuration;
using SensorSimulator.Generation;
using SensorSimulator.Publishing;

namespace SensorSimulator;

public sealed class Worker(
    ReadingGenerator generator,
    IReadingPublisher publisher,
    IOptions<SimulatorOptions> options,
    TimeProvider timeProvider,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(options.Value.IntervalSeconds);
        logger.LogInformation("Simulator publishing to {BaseUrl}{Path} every {Interval}",
            options.Value.IngestBaseUrl, options.Value.IngestPath, interval);

        // If a publish runs longer than the interval (e.g. retries), PeriodicTimer
        // coalesces the missed ticks instead of queuing a burst.
        using var timer = new PeriodicTimer(interval, timeProvider);

        do
        {
            var readings = generator.NextTick(timeProvider.GetUtcNow());
            if (readings.Count == 0)
            {
                continue;
            }

            try
            {
                await publisher.PublishAsync(readings, stoppingToken);
                logger.LogDebug("Published {Count} readings", readings.Count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Resilience handler already retried; drop this batch and keep simulating.
                logger.LogWarning("Failed to publish {Count} readings, dropping batch: {Error}", readings.Count, ex.Message);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
