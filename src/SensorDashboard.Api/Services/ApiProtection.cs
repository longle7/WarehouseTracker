using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SensorDashboard.Api.Data.Ingestion;

namespace SensorDashboard.Api.Services;

/// <summary>
/// Limits, timeouts and error handling for the whole API, registered in one place:
/// per-client rate limits (global + stricter ingest policy), request body cap, request
/// timeouts, and ProblemDetails for every failure.
/// </summary>
public static class ApiProtection
{
    public const string IngestPolicy = "ingest";
    public const string ClientErrorsPolicy = "client-errors";
    public const int MaxRequestBodyBytes = 1024 * 1024;

    public static WebApplicationBuilder AddApiProtection(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        services.AddValidatedOptions<ApiLimitsOptions>(ApiLimitsOptions.SectionName);

        // No request anywhere may carry more than 1 MB (ingest batches are ~200 KB at most).
        builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = MaxRequestBodyBytes);

        services.AddProblemDetails(o => o.CustomizeProblemDetails = ProblemDetailsTracing.AddTraceId);
        services.AddExceptionHandler<ApiExceptionHandler>();

        services.AddRequestTimeouts();
        services.AddOptions<RequestTimeoutOptions>().Configure<IOptions<ApiLimitsOptions>>((o, limits) =>
            o.DefaultPolicy = new RequestTimeoutPolicy
            {
                Timeout = limits.Value.RequestTimeout,
                TimeoutStatusCode = StatusCodes.Status504GatewayTimeout,
            });

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = (context, _) =>
            {
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait)
                    ? Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds))
                    : 1;
                context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString();
                return ValueTask.CompletedTask;
            };

            // Every endpoint: a generous per-client budget, so one runaway dashboard or script
            // can't starve everyone else. Health probes are exempt.
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            {
                if (http.Request.Path.StartsWithSegments("/health")) return RateLimitPartition.GetNoLimiter("health");
                var limits = http.RequestServices.GetRequiredService<IOptions<ApiLimitsOptions>>().Value;
                return TokenBucket(ClientKey(http), limits.RequestsPerSecond, limits.Burst);
            });

            // POST /ingest: stricter per-producer budget (429 + Retry-After is honored by the
            // simulator's Polly pipeline); queue-depth backpressure guards aggregate load.
            // Error reports from browsers: a crash loop in one tab can't flood the log.
            o.AddPolicy(ClientErrorsPolicy, http => RateLimitPartition.GetFixedWindowLimiter(ClientKey(http),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

            o.AddPolicy(IngestPolicy, http =>
            {
                var limits = http.RequestServices.GetRequiredService<IOptions<IngestionOptions>>().Value;
                return TokenBucket(ClientKey(http), limits.RateLimitPerSecond, limits.RateLimitBurst);
            });
        });

        services.AddSignalR(o =>
        {
            o.MaximumReceiveMessageSize = 32 * 1024; // clients only send small subscribe calls
            o.MaximumParallelInvocationsPerClient = 1;
            o.KeepAliveInterval = TimeSpan.FromSeconds(15);
            o.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
        });

        return builder;
    }

    public static WebApplication UseApiProtection(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages(); // bare 4xx/5xx (e.g. 404 for unknown routes) get ProblemDetails too
        app.UseRateLimiter();
        app.UseRequestTimeouts();
        return app;
    }

    private static string ClientKey(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static RateLimitPartition<string> TokenBucket(string key, int perSecond, int burst) =>
        RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = burst,
            TokensPerPeriod = perSecond,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueLimit = 0,
        });
}
