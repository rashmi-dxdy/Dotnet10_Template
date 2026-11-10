using BuildingBlocks.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orders.Infrastructure.Persistence;

namespace Orders.Infrastructure.DependencyInjection;

/// <summary>
/// Registration entry point for the Orders service persistence (its own DbContext and database).
/// </summary>
public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers <c>OrdersDbContext</c> against the database in <c>ConnectionStrings:OrdersDb</c> (tables prefixed
    /// <c>ORD_</c>), with the shared EF Core settings, a PostgreSQL health check and a startup connectivity check.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddOrdersPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddPostgresDbContext<OrdersDbContext>(
            configuration,
            OrdersDbContext.ConnectionStringName,
            OrdersDbContext.TablePrefix);

        // Extension point: register repository implementations of the Domain/Repositories interfaces here.
        return services;
    }
}
