namespace SensorSimulator;

/// <summary>
/// Named HttpClient for SensorDashboard.Api, used for ingestion and topology alike. Configured
/// once in Program.cs with the base address and the Polly resilience pipeline.
/// </summary>
public static class DashboardApiClient
{
    public const string Name = "DashboardApi";
}
