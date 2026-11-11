using Microsoft.AspNetCore.Routing;

namespace BuildingBlocks.Web.Modules;

/// <summary>
/// The route groups a module maps its endpoints on. Paths are relative to the group, e.g.
/// <c>routes.Internal.MapGet("products/{id:guid}", ...)</c> serves <c>/internal/catalog/products/{id}</c>.
/// </summary>
/// <param name="Public">
/// <c>/api/&lt;module&gt;</c>: the public API for external clients, listed in OpenAPI.
/// </param>
/// <param name="Internal">
/// <c>/internal/&lt;module&gt;</c>: the API other modules call through their module clients. Requires the internal
/// API key, hidden from OpenAPI, and never exposed by the gateway / ingress.
/// </param>
public sealed record ModuleRouteGroups(RouteGroupBuilder Public, RouteGroupBuilder Internal);
