using BuildingBlocks.Web.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Application.DependencyInjection;
using Notifications.Contracts;
using Notifications.Infrastructure.DependencyInjection;

namespace Notifications.Api;

/// <summary>
/// The Notifications module. The host runs it when <c>Modules:Notifications:Enabled</c> is true (the default); other modules call it
/// only through its internal API (<c>/internal/notifications</c>) and integration events, never through its types.
/// </summary>
public sealed class NotificationsModule : IModule
{
    /// <inheritdoc />
    public string Name => NotificationsModuleApi.Name;

    /// <inheritdoc />
    public void AddServices(IServiceCollection services, IConfiguration configuration) =>
        services
            .AddNotificationsApplication()
            .AddNotificationsInfrastructure(configuration);

    /// <inheritdoc />
    public void MapEndpoints(ModuleRouteGroups routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        // Extension points (see Endpoints/README.md and docs/modules.md):
        //   routes.Public   -> /api/notifications/...       public API for external clients (OpenAPI tag "notifications").
        //   routes.Internal -> /internal/notifications/...  API for other modules; DTOs from Notifications.Contracts.
        // Endpoints only translate HTTP to a command or query and send it with ISender.
    }
}
