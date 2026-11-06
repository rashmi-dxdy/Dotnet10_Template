using Microsoft.Extensions.DependencyInjection;

namespace Notifications.Application;

/// <summary>
/// Registration entry point for the Notifications application layer.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers Notifications application services. Extension point: add use cases, validators and
    /// application services here.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddNotificationsApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services;
    }
}
