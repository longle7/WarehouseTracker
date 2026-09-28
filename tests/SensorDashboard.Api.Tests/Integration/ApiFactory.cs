using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SensorDashboard.Api.Tests.Integration;

/// <summary>The real API (controllers, hosted services, SignalR) pointed at the test database.</summary>
public sealed class ApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:IoTDigitalTwin", connectionString);
        // Heartbeat pushes quickly so hub tests don't wait long.
        builder.UseSetting("Dashboard:LiveHeartbeat", "00:00:01");
    }
}
