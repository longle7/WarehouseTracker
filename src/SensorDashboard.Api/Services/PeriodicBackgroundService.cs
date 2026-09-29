namespace SensorDashboard.Api.Services;

/// <summary>
/// Runs <see cref="RunOnceAsync"/> immediately and then every <see cref="Interval"/>. A failed
/// run is logged and retried on the next tick; shutdown stops the loop cleanly.
/// </summary>
public abstract class PeriodicBackgroundService(TimeProvider timeProvider, ILogger logger) : BackgroundService
{
    /// <summary>The clock the timer runs on; derived services read the time from it too.</summary>
    protected TimeProvider Clock { get; } = timeProvider;

    protected abstract TimeSpan Interval { get; }

    protected abstract Task RunOnceAsync(CancellationToken cancellationToken);

    protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, Clock);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "{Service} run failed; retrying in {Interval}", GetType().Name, Interval);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
