using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SensorDashboard.Api.Data.Telemetry;

namespace SensorDashboard.Api.Tests.Integration;

/// <summary>Limits, timeouts and error handling through the real pipeline, with failures injected.</summary>
[Collection(SqlServerCollection.Name)]
public class ApiProtectionTests(SqlServerFixture db)
{
    private WebApplicationFactory<Program> Api(Func<CancellationToken, Task>? onQuery = null, params (string Key, string Value)[] settings) =>
        new ApiFactory(db.ConnectionString).WithWebHostBuilder(b =>
        {
            foreach (var (key, value) in settings) b.UseSetting(key, value);
            if (onQuery is not null)
            {
                b.ConfigureTestServices(s => s.AddSingleton<IReadingQueries>(new FaultyQueries(onQuery)));
            }
        });

    [Fact]
    public async Task Unexpected_errors_return_problem_details_with_a_trace_id_and_no_internals()
    {
        await using var api = Api(_ => throw new InvalidOperationException("secret connection detail"));

        var response = await api.CreateClient().GetAsync("/warehouses");
        var body = await response.Content.ReadAsStringAsync();
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("An unexpected error occurred.", problem!.Title);
        Assert.True(problem.Extensions.ContainsKey("traceId"));
        Assert.DoesNotContain("secret connection detail", body);
        Assert.DoesNotContain("   at ", body); // no stack trace
    }

    [Fact]
    public async Task Timeouts_in_dependencies_return_504()
    {
        await using var api = Api(_ => throw new TimeoutException());

        var response = await api.CreateClient().GetAsync("/warehouses");

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
    }

    [Fact]
    public async Task Requests_that_run_too_long_are_cut_off_with_504()
    {
        await using var api = Api(ct => Task.Delay(TimeSpan.FromSeconds(30), ct), ("ApiLimits:RequestTimeout", "00:00:01"));

        var started = DateTime.UtcNow;
        var response = await api.CreateClient().GetAsync("/warehouses");

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Unknown_routes_return_problem_details()
    {
        await using var api = Api();

        var response = await api.CreateClient().GetAsync("/no-such-route");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Clients_over_the_global_rate_limit_get_429_but_health_probes_do_not()
    {
        await using var api = Api(null, ("ApiLimits:RequestsPerSecond", "1"), ("ApiLimits:Burst", "2"));
        var client = api.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++) statuses.Add((await client.GetAsync("/warehouses/WH-SEA/scene")).StatusCode);
        var limited = await client.GetAsync("/warehouses/WH-SEA/scene");

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK], statuses.Take(2));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    private sealed class FaultyQueries(Func<CancellationToken, Task> onQuery) : IReadingQueries
    {
        public async Task<IReadOnlyDictionary<string, LatestReading>> GetLatestAsync(
            IReadOnlyCollection<string> sensorIds, DateTimeOffset since, CancellationToken cancellationToken)
        {
            await onQuery(cancellationToken);
            return new Dictionary<string, LatestReading>();
        }

        public Task<IReadOnlyDictionary<string, ConditionStreaks>> GetConditionStreaksAsync(
            IReadOnlyCollection<SensorThresholds> sensors, DateTimeOffset since, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, ConditionStreaks>>(new Dictionary<string, ConditionStreaks>());

        public Task<IReadOnlyList<ReadingBucket>> GetHistoryAsync(
            string sensorId, DateTimeOffset from, DateTimeOffset to, TimeSpan bucket, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReadingBucket>>([]);
    }
}
