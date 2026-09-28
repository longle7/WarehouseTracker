namespace SensorDashboard.Api.Services;

public sealed record HistoryRange(DateTimeOffset From, DateTimeOffset To, TimeSpan Bucket);

/// <summary>Fills in defaults for a history request, validates it, and picks a bucket size.</summary>
public static class HistoryRangeResolver
{
    // Bucket sizes a chart axis reads naturally.
    public static readonly IReadOnlyList<TimeSpan> NiceBuckets =
    [
        .. new[] { 1, 5, 10, 15, 30 }.Select(s => TimeSpan.FromSeconds(s)),
        .. new[] { 1, 5, 10, 15, 30 }.Select(m => TimeSpan.FromMinutes(m)),
        .. new[] { 1, 3, 6, 12, 24 }.Select(h => TimeSpan.FromHours(h)),
    ];

    /// <returns>A range, or an error message when the request is invalid.</returns>
    public static (HistoryRange? Range, string? Error) Resolve(
        DateTimeOffset? from, DateTimeOffset? to, int? bucketSeconds, DateTimeOffset now, DashboardOptions options)
    {
        var end = to ?? now;
        var start = from ?? end - options.DefaultHistoryWindow;
        var window = end - start;

        if (window <= TimeSpan.Zero)
            return (null, "'from' must be earlier than 'to'.");
        if (window > options.MaxHistoryWindow)
            return (null, $"The range can't exceed {options.MaxHistoryWindow.TotalDays:0} days.");
        if (bucketSeconds is < 1 or > 86_400)
            return (null, "'bucketSeconds' must be between 1 and 86400.");

        var bucket = bucketSeconds is { } s
            ? TimeSpan.FromSeconds(s)
            : NiceBuckets.FirstOrDefault(b => window / b <= options.TargetHistoryPoints, NiceBuckets[^1]);

        return (new HistoryRange(start, end, bucket), null);
    }
}
