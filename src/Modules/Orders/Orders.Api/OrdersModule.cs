using BuildingBlocks.Web.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orders.Application.DependencyInjection;
using Orders.Contracts;
using Orders.Infrastructure.DependencyInjection;

namespace Orders.Api;

/// <summary>
/// The Orders module. The host runs it when <c>Modules:Orders:Enabled</c> is true (the default); other modules call it
/// only through its internal API (<c>/internal/orders</c>) and integration events, never through its types.
/// </summary>
public sealed class OrdersModule : IModule
{
    /// <inheritdoc />
    public string Name => OrdersModuleApi.Name;

    /// <inheritdoc />
    public void AddServices(IServiceCollection services, IConfiguration configuration) =>
        services
            .AddOrdersApplication()
            .AddOrdersInfrastructure(configuration);

    /// <inheritdoc />
    public void MapEndpoints(ModuleRouteGroups routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        // Extension points (see Endpoints/README.md and docs/modules.md):
        //   routes.Public   -> /api/orders/...       public API for external clients (OpenAPI tag "orders").
        //   routes.Internal -> /internal/orders/...  API for other modules; DTOs from Orders.Contracts.
        // Endpoints only translate HTTP to a command or query and send it with ISender.
    }
}
