using System.Net;
using System.Net.Http.Json;
using IoTDigitalTwin.Contracts;
using IoTDigitalTwin.Contracts.Monitoring;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using static SensorDashboard.Api.Tests.Integration.TestData;

namespace SensorDashboard.Api.Tests.Integration;

[Collection(SqlServerCollection.Name)]
public class MonitoringTests(SqlServerFixture db) : IAsyncLifetime
{
    public Task InitializeAsync() => db.ResetTelemetryAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Status_reports_live_checks_and_degrades_when_no_readings_arrive()
    {
        await using var factory = new ApiFactory(db.ConnectionString);
        var client = factory.CreateClient();

        var status = await client.GetFromJsonAsync<SystemStatusDto>("/status", ContractJson.Options);

        Assert.Equal(["database", "ingest-queue", "data-freshness"], status!.Checks.Select(c => c.Name));
        Assert.Equal(HealthState.Healthy, status.Checks.Single(c => c.Name == "database").Status);
        // Nothing has been ingested by this API instance yet.
        Assert.Equal(HealthState.Degraded, status.Checks.Single(c => c.Name == "data-freshness").Status);
        Assert.Equal(HealthState.Degraded, status.Status);
    }

    [Fact]
    public async Task Readings_make_data_fresh_again()
    {
        await using var factory = new ApiFactory(db.ConnectionString);
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/ingest", new[] { Reading(SeaDairy, Now()) });

        await Eventually.Until(async () =>
        {
            // Bypass the 2 s output cache by waiting it out between polls.
            var status = await client.GetFromJsonAsync<SystemStatusDto>("/status", ContractJson.Options);
            return status!.Checks.Single(c => c.Name == "data-freshness").Status == HealthState.Healthy;
        });
    }

    [Fact]
    public async Task Sampler_records_history_and_status_derives_uptime_and_incidents()
    {
        // Seed a known history: healthy, then a 2-sample outage, then healthy again.
        var t0 = DateTime.UtcNow.AddMinutes(-10);
        await db.ExecuteAsync($"""
            INSERT ops.HealthSamples (CheckedAt, Status, DurationMs, Failing) VALUES
              ('{t0:O}', 'Healthy', 5, ''),
              ('{t0.AddMinutes(1):O}', 'Unhealthy', 5, 'database: Database unreachable.'),
              ('{t0.AddMinutes(2):O}', 'Unhealthy', 5, 'database: Database unreachable.'),
              ('{t0.AddMinutes(3):O}', 'Healthy', 5, '');
            """);
        await using var factory = new ApiFactory(db.ConnectionString).WithWebHostBuilder(b =>
            b.UseSetting("Monitoring:SampleInterval", "00:00:01"));
        var client = factory.CreateClient();

        // The sampler adds live samples on top of the seeded ones.
        await Eventually.Until(async () => await db.ScalarAsync<int>("SELECT COUNT(*) FROM ops.HealthSamples") > 4);
        var status = await client.GetFromJsonAsync<SystemStatusDto>("/status", ContractJson.Options);

        var incident = Assert.Single(status!.Incidents, i => i.Status == HealthState.Unhealthy);
        Assert.Equal("database: Database unreachable.", incident.Summary);
        Assert.NotNull(incident.EndedAt);
        Assert.InRange(status.Uptime24h!.Value, 1, 99.99); // two of the samples were down
    }

    [Fact]
    public async Task Client_errors_are_accepted_and_rate_limited()
    {
        await using var factory = new ApiFactory(db.ConnectionString);
        var client = factory.CreateClient();
        var report = new ClientErrorReportDto("Cannot read properties of undefined", "at App.tsx:12", "ErrorBoundary", "/warehouses");

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 11; i++) statuses.Add((await client.PostAsJsonAsync("/client-errors", report)).StatusCode);

        Assert.All(statuses.Take(10), s => Assert.Equal(HttpStatusCode.NoContent, s));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[10]);
    }
}
