using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using IoTDigitalTwin.Contracts.Alerts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using SensorDashboard.Api.Data.Telemetry;
using static SensorDashboard.Api.Tests.Integration.TestData;

namespace SensorDashboard.Api.Tests.Integration;

[Collection(SqlServerCollection.Name)]
public class AlertLifecycleTests(SqlServerFixture db) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public async Task InitializeAsync()
    {
        await db.ResetTelemetryAsync();
        await db.ExecuteAsync("DELETE ops.Alerts;");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Streak_query_finds_when_the_current_excursion_began()
    {
        var now = Now();
        await new SqlReadingWriter(db.ConnectionString).WriteAsync(
        [
            Reading(SeaDairy, now.AddSeconds(-30), temperature: 37),
            Reading(SeaDairy, now.AddSeconds(-20), temperature: 43, doorOpen: true),
            Reading(SeaDairy, now.AddSeconds(-10), temperature: 46, doorOpen: true),
            Reading(SeaDairy, now, temperature: 44),
            Reading(SeaProduce, now, temperature: 37),
        ], default);

        var streaks = await new SqlReadingQueries(db.ConnectionString).GetConditionStreaksAsync(
            [new(SeaDairy, 34, 40), new(SeaProduce, 34, 40)], now.AddHours(-1), default);

        var dairy = streaks[SeaDairy];
        Assert.Equal(now.AddSeconds(-20), dairy.TemperatureOutOfRangeSince);
        Assert.Equal((43.0, 46.0), (dairy.StreakMin, dairy.StreakMax));
        Assert.Null(dairy.DoorOpenSince); // latest reading has the door closed
        Assert.Equal(now, dairy.LastReadingAt);
        Assert.Null(streaks[SeaProduce].TemperatureOutOfRangeSince);
    }

    [Fact]
    public async Task Alert_opens_is_acknowledged_and_resolves_through_the_api()
    {
        await using var factory = new ApiFactory(db.ConnectionString).WithWebHostBuilder(b =>
        {
            b.UseSetting("Alerts:EvaluationInterval", "00:00:00.500");
            b.UseSetting("Alerts:TemperatureGracePeriod", "00:00:00");
        });
        var client = factory.CreateClient();

        // 10°F over: critical.
        await client.PostAsJsonAsync("/ingest", new[] { Reading(SeaDairy, Now(), temperature: 50) });
        var alert = await WaitForAlertAsync(client, a => a.Kind == AlertKind.TemperatureOutOfRange && a.SensorId == SeaDairy);
        Assert.Equal((AlertState.Open, AlertSeverity.Critical, 50.0), (alert.State, alert.Severity, alert.PeakTemperature));
        Assert.Equal("Dairy Cooler", alert.Location);

        var ack = await client.PostAsJsonAsync($"/alerts/{alert.Id}/acknowledge", new AcknowledgeAlertDto("Sam"));
        var acknowledged = await ack.Content.ReadFromJsonAsync<AlertDto>(Json);
        Assert.Equal((AlertState.Acknowledged, "Sam"), (acknowledged!.State, acknowledged.AcknowledgedBy));

        // Back in range: resolved, and acknowledging it now conflicts.
        await client.PostAsJsonAsync("/ingest", new[] { Reading(SeaDairy, Now().AddSeconds(1), temperature: 37) });
        var resolved = await WaitForAlertAsync(client, a => a.Id == alert.Id && a.State == AlertState.Resolved, state: "resolved");
        Assert.NotNull(resolved.ClosedAt);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/alerts/{alert.Id}/acknowledge", new AcknowledgeAlertDto(null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/alerts/999999/acknowledge", new AcknowledgeAlertDto(null))).StatusCode);
    }

    private static async Task<AlertDto> WaitForAlertAsync(HttpClient client, Func<AlertDto, bool> match, string state = "active")
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (true)
        {
            var alerts = await client.GetFromJsonAsync<List<AlertDto>>($"/alerts?state={state}&warehouseId={Seattle}", Json);
            if (alerts!.FirstOrDefault(match) is { } found) return found;
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Expected alert did not appear.");
            await Task.Delay(200);
        }
    }
}
