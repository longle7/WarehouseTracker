namespace SensorDashboard.Api.Tests.Integration;

/// <summary>
/// Polling for effects that happen asynchronously (queued ingestion, background evaluators,
/// SignalR pushes): re-read until a condition holds, or fail with a timeout.
/// </summary>
internal static class Eventually
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    /// <returns>The first value that satisfies <paramref name="done"/>.</returns>
    public static async Task<T> Until<T>(Func<Task<T>> read, Func<T, bool> done, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (true)
        {
            var value = await read();
            if (done(value)) return value;
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Condition not met within {timeout ?? DefaultTimeout}.");
            await Task.Delay(PollInterval);
        }
    }

    public static Task Until(Func<Task<bool>> condition, TimeSpan? timeout = null) =>
        Until(condition, satisfied => satisfied, timeout);
}
