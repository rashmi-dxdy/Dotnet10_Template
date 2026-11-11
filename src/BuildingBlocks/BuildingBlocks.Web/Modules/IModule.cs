using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Web.Modules;

/// <summary>
/// A module: one bounded context (its Domain, Application and Infrastructure) plugged into the host. Each module's
/// <c>&lt;Module&gt;.Api</c> project implements it once. The host lists every module it can run; configuration
/// (<c>Modules:&lt;Name&gt;:Enabled</c>) decides which ones run in a given process.
/// </summary>
public interface IModule
{
    /// <summary>
    /// Gets the module name, e.g. <c>catalog</c>: lowercase letters, digits and hyphens. It names the routes
    /// (<c>/api/catalog</c>, <c>/internal/catalog</c>), the configuration section (<c>Modules:catalog</c>) and the
    /// <c>Module</c> log property. Keep it in the module's Contracts project so callers use the same value.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Registers the module's services: its Application and Infrastructure layers. Called only when the module is
    /// enabled in this process.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    void AddServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>
    /// Maps the module's HTTP endpoints. Called only when the module is enabled in this process.
    /// </summary>
    /// <param name="routes">The module's public and internal route groups.</param>
    void MapEndpoints(ModuleRouteGroups routes);
}
