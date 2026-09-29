using Microsoft.Extensions.Options;
using SensorSimulator.Configuration;
using SensorSimulator.Generation;
using SensorSimulator.Publishing;
using SensorSimulator.Topology;

namespace SensorSimulator;

public sealed class Worker(
    ITopologySource topologySource,
    StoreAndForward delivery,
    IOptions<SimulatorOptions> options,
    TimeProvider timeProvider,
    ILoggerFactory loggerFactory,
    ILogger<Worker> logger) : BackgroundService
{
    private static readonly TimeSpan TopologyRetryDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var warehouses = await LoadTopologyAsync(stoppingToken);
        var generator = new ReadingGenerator(warehouses, options, loggerFactory.CreateLogger<ReadingGenerator>());

        var interval = TimeSpan.FromSeconds(options.Value.IntervalSeconds);
        logger.LogInformation("Simulating {Sensors} sensors in {Warehouses} warehouses; publishing to {BaseUrl}{Path} every {Interval}",
            warehouses.Sum(w => w.Sensors.Count), warehouses.Count, options.Value.IngestBaseUrl, options.Value.IngestPath, interval);

        using var timer = new PeriodicTimer(interval, timeProvider);

        do
        {
            // Sampling never waits on the network: delivery happens on StoreAndForward's own loop.
            delivery.Enqueue(generator.NextTick(timeProvider.GetUtcNow()));
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>
    /// Loads the warehouses and sensors to simulate, retrying until the source answers with at
    /// least one active sensor (the API may still be starting, e.g. under docker compose).
    /// </summary>
    private async Task<IReadOnlyList<Warehouse>> LoadTopologyAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            try
            {
                var warehouses = await topologySource.LoadAsync(stoppingToken);
                if (warehouses.Sum(w => w.Sensors.Count) > 0)
                {
                    return warehouses;
                }
                logger.LogWarning("Topology has no active sensors yet; retrying in {Delay}", TopologyRetryDelay);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Couldn't load topology ({Error}); retrying in {Delay}", ex.Message, TopologyRetryDelay);
            }
            await Task.Delay(TopologyRetryDelay, timeProvider, stoppingToken);
        }
    }
}
