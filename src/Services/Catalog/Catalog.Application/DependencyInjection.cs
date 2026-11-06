using Microsoft.Extensions.DependencyInjection;

namespace Catalog.Application;

/// <summary>
/// Registration entry point for the Catalog application layer.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers Catalog application services. Extension point: add use cases, validators and
    /// application services here.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddCatalogApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services;
    }
}
