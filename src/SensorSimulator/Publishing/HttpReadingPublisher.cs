using System.Net.Http.Json;
using IoTDigitalTwin.Contracts.Telemetry;
using Microsoft.Extensions.Options;
using SensorSimulator.Configuration;

namespace SensorSimulator.Publishing;

/// <summary>
/// POSTs each tick's batch to the API's ingestion endpoint. Retries, timeouts and the
/// circuit breaker come from the resilience handler on the DashboardApi client (see Program.cs).
/// </summary>
public sealed class HttpReadingPublisher(IHttpClientFactory httpClientFactory, IOptions<SimulatorOptions> options)
    : IReadingPublisher
{
    public async Task PublishAsync(IReadOnlyList<SensorReadingDto> readings, string idempotencyKey, CancellationToken cancellationToken)
    {
        // Create per call rather than holding one HttpClient in this singleton, so the
        // factory can rotate handlers (DNS changes, connection lifetime).
        var client = httpClientFactory.CreateClient(DashboardApiClient.Name);
        using var request = new HttpRequestMessage(HttpMethod.Post, options.Value.IngestPath)
        {
            Content = JsonContent.Create(readings),
        };
        // Polly retries resend this same message, and store-and-forward reuses the key, so the
        // API queues each batch once however many times it's delivered.
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
