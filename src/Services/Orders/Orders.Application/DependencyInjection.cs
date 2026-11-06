using Microsoft.Extensions.DependencyInjection;

namespace Orders.Application;

/// <summary>
/// Registration entry point for the Orders application layer.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers Orders application services. Extension point: add use cases, validators and
    /// application services here.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddOrdersApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services;
    }
}
