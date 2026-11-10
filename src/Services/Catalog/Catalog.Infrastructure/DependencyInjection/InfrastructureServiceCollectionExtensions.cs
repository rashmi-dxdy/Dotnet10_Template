using BuildingBlocks.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Catalog.Infrastructure.DependencyInjection;

/// <summary>
/// Registration entry point for the Catalog infrastructure layer.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers Catalog infrastructure services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddCatalogInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddBuildingBlocksInfrastructure(configuration);
        services.AddCatalogPersistence(configuration);
        services.AddCatalogMessaging(configuration);

        // Extension points:
        //   Persistence/       -> entity configurations, migrations, repositories (DbContext: AddCatalogPersistence).
        //   Messaging/         -> message handlers and outbox (consumers: AddCatalogMessaging).
        //   ExternalServices/  -> HTTP and third-party clients.
        return services;
    }
}
