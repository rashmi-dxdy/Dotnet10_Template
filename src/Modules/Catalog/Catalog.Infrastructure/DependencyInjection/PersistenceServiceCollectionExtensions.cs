using BuildingBlocks.Infrastructure.DependencyInjection;
using Catalog.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Catalog.Infrastructure.DependencyInjection;

/// <summary>
/// Registration entry point for the Catalog module persistence (its own DbContext and database).
/// </summary>
public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers <c>CatalogDbContext</c> against the database in <c>ConnectionStrings:CatalogDb</c> (tables prefixed
    /// <c>CAT_</c>), with the shared EF Core settings, a PostgreSQL health check and a startup connectivity check.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddCatalogPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddPostgresDbContext<CatalogDbContext>(
            configuration,
            CatalogDbContext.ConnectionStringName,
            CatalogDbContext.TablePrefix);

        // Extension point: register repository implementations of the Domain/Repositories interfaces here.
        return services;
    }
}
