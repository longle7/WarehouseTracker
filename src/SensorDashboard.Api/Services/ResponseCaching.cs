namespace SensorDashboard.Api.Services;

/// <summary>
/// Output caching for repeat GETs: many dashboards polling the same live endpoints within a
/// couple of seconds share one computed response, and the static 3D scene is served from
/// memory. Tags let writes evict what they change.
/// </summary>
public static class ResponseCaching
{
    /// <summary>Live summaries (warehouses, sensors): fresh within 2 s.</summary>
    public const string Live = "live";

    /// <summary>Alert lists, per filter; evicted when an alert is acknowledged.</summary>
    public const string Alerts = "alerts";

    /// <summary>Static metadata (the 3D scene layout).</summary>
    public const string Static = "static";

    public const string AlertsTag = "alerts";

    public static IServiceCollection AddApiOutputCache(this IServiceCollection services) =>
        services.AddOutputCache(o =>
        {
            o.AddPolicy(Live, b => b.Expire(TimeSpan.FromSeconds(2)));
            o.AddPolicy(Alerts, b => b.Expire(TimeSpan.FromSeconds(2)).SetVaryByQuery("state", "warehouseId", "limit").Tag(AlertsTag));
            o.AddPolicy(Static, b => b.Expire(TimeSpan.FromMinutes(10)));
        });
}
