using Microsoft.Extensions.Options;

namespace SensorDashboard.Api.Services;

public static class OptionsRegistration
{
    /// <summary>
    /// Binds a configuration section and validates it (data annotations) when the host starts,
    /// so a bad value such as a zero interval fails fast with a clear message instead of
    /// crashing a background service later.
    /// </summary>
    public static OptionsBuilder<T> AddValidatedOptions<T>(this IServiceCollection services, string sectionName)
        where T : class =>
        services.AddOptions<T>().BindConfiguration(sectionName).ValidateDataAnnotations().ValidateOnStart();
}
