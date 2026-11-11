using BuildingBlocks.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Infrastructure.Persistence;

namespace Notifications.Infrastructure.DependencyInjection;

/// <summary>
/// Registration entry point for the Notifications module persistence (its own DbContext and database).
/// </summary>
public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers <c>NotificationsDbContext</c> against the database in <c>ConnectionStrings:NotificationsDb</c> (tables prefixed
    /// <c>NTF_</c>), with the shared EF Core settings, a PostgreSQL health check and a startup connectivity check.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddNotificationsPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddPostgresDbContext<NotificationsDbContext>(
            configuration,
            NotificationsDbContext.ConnectionStringName,
            NotificationsDbContext.TablePrefix);

        // Extension point: register repository implementations of the Domain/Repositories interfaces here.
        return services;
    }
}
