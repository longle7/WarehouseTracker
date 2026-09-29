using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;

namespace SensorDashboard.Api.Tests.Integration;

/// <summary>Bad configuration stops the API at startup with a message naming the setting.</summary>
[Collection(SqlServerCollection.Name)]
public class OptionsValidationTests(SqlServerFixture db)
{
    [Theory]
    [InlineData("Telemetry:RollupRefreshInterval", "00:00:00", "RollupRefreshInterval")]
    [InlineData("Ingestion:MaxAttempts", "0", "MaxAttempts")]
    [InlineData("Alerts:EvaluationInterval", "00:00:00", "EvaluationInterval")]
    [InlineData("Dashboard:DefaultHistoryWindow", "40.00:00:00", "DefaultHistoryWindow")]
    public void Invalid_settings_fail_startup(string key, string value, string expectedInMessage)
    {
        using var factory = new ApiFactory(db.ConnectionString).WithWebHostBuilder(b => b.UseSetting(key, value));

        var error = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Contains(expectedInMessage, error.Message);
    }
}
