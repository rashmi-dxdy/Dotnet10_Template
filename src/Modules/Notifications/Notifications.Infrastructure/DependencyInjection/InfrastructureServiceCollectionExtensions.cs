using BuildingBlocks.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Notifications.Infrastructure.DependencyInjection;

/// <summary>
/// Registration entry point for the Notifications infrastructure layer.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers Notifications infrastructure services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddNotificationsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddBuildingBlocksInfrastructure(configuration);
        services.AddNotificationsPersistence(configuration);
        services.AddNotificationsMessaging(configuration);
        services.AddNotificationsStreaming(configuration);

        // Extension points:
        //   Persistence/       -> entity configurations, migrations, repositories (DbContext: AddNotificationsPersistence).
        //   Messaging/         -> message handlers and outbox (consumers: AddNotificationsMessaging, AddNotificationsStreaming).
        //   ExternalServices/  -> clients of OTHER modules' internal APIs (AddModuleClient, DTOs from
        //                         <Module>.Contracts) and third-party clients. See docs/modules.md.
        return services;
    }
}
