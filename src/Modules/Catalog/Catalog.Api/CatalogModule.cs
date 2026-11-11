using BuildingBlocks.Web.Modules;
using Catalog.Application.DependencyInjection;
using Catalog.Contracts;
using Catalog.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Catalog.Api;

/// <summary>
/// The Catalog module. The host runs it when <c>Modules:Catalog:Enabled</c> is true (the default); other modules call it
/// only through its internal API (<c>/internal/catalog</c>) and integration events, never through its types.
/// </summary>
public sealed class CatalogModule : IModule
{
    /// <inheritdoc />
    public string Name => CatalogModuleApi.Name;

    /// <inheritdoc />
    public void AddServices(IServiceCollection services, IConfiguration configuration) =>
        services
            .AddCatalogApplication()
            .AddCatalogInfrastructure(configuration);

    /// <inheritdoc />
    public void MapEndpoints(ModuleRouteGroups routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        // Extension points (see Endpoints/README.md and docs/modules.md):
        //   routes.Public   -> /api/catalog/...       public API for external clients (OpenAPI tag "catalog").
        //   routes.Internal -> /internal/catalog/...  API for other modules; DTOs from Catalog.Contracts.
        // Endpoints only translate HTTP to a command or query and send it with ISender.
    }
}
