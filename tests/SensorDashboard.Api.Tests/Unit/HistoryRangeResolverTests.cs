using SensorDashboard.Api.Services;

namespace SensorDashboard.Api.Tests.Unit;

public class HistoryRangeResolverTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly DashboardOptions Options = new();

    [Fact]
    public void Defaults_to_the_last_hour_in_15_second_buckets()
    {
        var (range, error) = HistoryRangeResolver.Resolve(null, null, null, Now, Options);

        Assert.Null(error);
        Assert.Equal(Now.AddHours(-1), range!.From);
        Assert.Equal(Now, range.To);
        Assert.Equal(TimeSpan.FromSeconds(15), range.Bucket); // 3600s / 15s = 240 points <= 300
    }

    [Theory]
    [InlineData(15, 5)] // 900s / 5s = 180 points
    [InlineData(360, 300)] // 6h: 1-minute buckets would be 360 points, so 5 minutes (72)
    [InlineData(1440, 300)] // 24h / 5m = 288 points
    public void Picks_the_smallest_nice_bucket_within_the_point_budget(int windowMinutes, int expectedBucketSeconds)
    {
        var (range, _) = HistoryRangeResolver.Resolve(Now.AddMinutes(-windowMinutes), Now, null, Now, Options);

        Assert.Equal(TimeSpan.FromSeconds(expectedBucketSeconds), range!.Bucket);
    }

    [Fact]
    public void Explicit_bucket_is_used_as_given()
    {
        var (range, _) = HistoryRangeResolver.Resolve(null, null, 60, Now, Options);
        Assert.Equal(TimeSpan.FromMinutes(1), range!.Bucket);
    }

    [Theory]
    [InlineData(0, -1, null, "'from' must be earlier")]
    [InlineData(-32 * 24 * 60, 0, null, "can't exceed")]
    [InlineData(-60, 0, 0, "bucketSeconds")]
    [InlineData(-60, 0, 100_000, "bucketSeconds")]
    public void Invalid_requests_return_an_error(int fromMinutes, int toMinutes, int? bucket, string message)
    {
        var (range, error) = HistoryRangeResolver.Resolve(Now.AddMinutes(fromMinutes), Now.AddMinutes(toMinutes), bucket, Now, Options);

        Assert.Null(range);
        Assert.Contains(message, error);
    }
}
