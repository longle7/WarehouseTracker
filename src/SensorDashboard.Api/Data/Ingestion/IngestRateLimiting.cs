using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace SensorDashboard.Api.Data.Ingestion;

public static class IngestRateLimiting
{
    public const string Policy = "ingest";

    /// <summary>
    /// Per-client token bucket on POST /ingest: a misbehaving producer gets 429 with Retry-After
    /// (which the simulator's Polly pipeline honors) instead of flooding the queue and database.
    /// Complements queue-depth backpressure, which protects against aggregate overload.
    /// </summary>
    public static IServiceCollection AddIngestRateLimiting(this IServiceCollection services) =>
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
            o.AddPolicy(Policy, http =>
            {
                var limits = http.RequestServices.GetRequiredService<IOptions<IngestionOptions>>().Value;
                return RateLimitPartition.GetTokenBucketLimiter(
                    http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = limits.RateLimitBurst,
                        TokensPerPeriod = limits.RateLimitPerSecond,
                        ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                        QueueLimit = 0,
                    });
            });
        });
}
