using Microsoft.Extensions.Options;
using SensorSimulator;
using SensorSimulator.Configuration;
using SensorSimulator.Publishing;
using SensorSimulator.Topology;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddOptions<SimulatorOptions>()
    .BindConfiguration(SimulatorOptions.SectionName)
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<SimulatorOptions>, SimulatorOptionsValidator>();

builder.Services.AddSingleton(TimeProvider.System);

// Topology: the API is the source of truth; the built-in seed list is the offline fallback.
builder.Services.AddSingleton<ITopologySource>(sp =>
    sp.GetRequiredService<IOptions<SimulatorOptions>>().Value.TopologySource == TopologySourceKind.Seed
        ? new SeedTopologySource()
        : ActivatorUtilities.CreateInstance<ApiTopologySource>(sp));

// Standard pipeline (Polly v8): rate limiter, total timeout, retry with exponential
// backoff + jitter, circuit breaker, per-attempt timeout.
builder.Services.AddHttpClient(HttpReadingPublisher.HttpClientName, (sp, client) =>
        client.BaseAddress = new Uri(sp.GetRequiredService<IOptions<SimulatorOptions>>().Value.IngestBaseUrl))
    .AddStandardResilienceHandler(o =>
    {
        // The defaults suit high-traffic clients: the breaker needs 100 requests per 30s
        // window before it can trip, which one-batch-per-tick never reaches.
        o.CircuitBreaker.MinimumThroughput = 5;
        o.CircuitBreaker.FailureRatio = 0.5;
        o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
        o.Retry.Delay = TimeSpan.FromMilliseconds(500);
    });
builder.Services.AddSingleton<IReadingPublisher, HttpReadingPublisher>();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
