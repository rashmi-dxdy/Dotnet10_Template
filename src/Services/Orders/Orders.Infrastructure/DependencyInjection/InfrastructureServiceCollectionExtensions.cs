using BuildingBlocks.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Orders.Infrastructure.DependencyInjection;

/// <summary>
/// Registration entry point for the Orders infrastructure layer.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers Orders infrastructure services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddOrdersInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddBuildingBlocksInfrastructure(configuration);
        services.AddOrdersPersistence(configuration);
        services.AddOrdersMessaging(configuration);

        // Extension points:
        //   Persistence/       -> entity configurations, migrations, repositories (DbContext: AddOrdersPersistence).
        //   Messaging/         -> message handlers and outbox (consumers: AddOrdersMessaging).
        //   ExternalServices/  -> HTTP and third-party clients.
        return services;
    }
}
