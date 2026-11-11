using System.Net.Http.Json;
using BuildingBlocks.Abstractions.Correlation;
using BuildingBlocks.Infrastructure.DependencyInjection;
using BuildingBlocks.Infrastructure.Modules;
using BuildingBlocks.Web.DependencyInjection;
using BuildingBlocks.Web.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.IntegrationTests.Modules;

/// <summary>What the "inventory" module's internal API returns.</summary>
/// <param name="Text">The echoed text.</param>
/// <param name="CorrelationId">The correlation ID the request arrived with.</param>
/// <param name="ServedBy">Which host answered.</param>
internal sealed record EchoResponse(string Text, string? CorrelationId, string ServedBy);

/// <summary>A module with an internal API only: <c>GET /internal/inventory/echo/{text}</c>.</summary>
internal sealed class InventoryModule : IModule
{
    public const string ModuleName = "inventory";

    public string Name => ModuleName;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        // No services: the module only answers internal calls.
    }

    public void MapEndpoints(ModuleRouteGroups routes) =>
        routes.Internal.MapGet("echo/{text}", (string text, HttpContext context, IConfiguration configuration) =>
            new EchoResponse(
                text,
                context.Request.Headers[CorrelationHeader.Name].ToString(),
                configuration["HostName"] ?? "?"));
}

/// <summary>
/// A module whose public endpoint (<c>GET /api/shop/echo/{text}</c>) calls the inventory module's internal API
/// through a module client, the way a real module calls another one.
/// </summary>
internal sealed class ShopModule : IModule
{
    public string Name => "shop";

    public void AddServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddModuleClient<IInventoryGateway, InventoryHttpGateway>(configuration, InventoryModule.ModuleName);

    public void MapEndpoints(ModuleRouteGroups routes) =>
        routes.Public.MapGet("echo/{text}", (string text, IInventoryGateway inventory, CancellationToken token) =>
            inventory.EchoAsync(text, token));
}

/// <summary>The port the shop module's Application layer would define.</summary>
internal interface IInventoryGateway
{
    Task<EchoResponse> EchoAsync(string text, CancellationToken cancellationToken);
}

/// <summary>The HTTP implementation the shop module's Infrastructure layer would provide.</summary>
/// <param name="http">The module client.</param>
internal sealed class InventoryHttpGateway(HttpClient http) : IInventoryGateway
{
    public async Task<EchoResponse> EchoAsync(string text, CancellationToken cancellationToken)
    {
        using var response = await http
            .GetAsync(new Uri($"echo/{Uri.EscapeDataString(text)}", UriKind.Relative), cancellationToken)
            .ConfigureAwait(false);
        await response.EnsureModuleSuccessAsync(InventoryModule.ModuleName, cancellationToken).ConfigureAwait(false);

        return await response.Content.ReadFromJsonAsync<EchoResponse>(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Empty response.");
    }
}

/// <summary>Starts a real host (Kestrel on a free loopback port) the way <c>Host.Api</c> composes modules.</summary>
internal static class ModuleTestHost
{
    public static async Task<WebApplication> StartAsync(Dictionary<string, string?> settings, params IModule[] modules)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(settings);

        builder.AddWebServiceDefaults();
        builder.AddModules(modules);

        var app = builder.Build();
        app.UseWebServiceDefaults();
        app.MapModules();

        try
        {
            await app.StartAsync(TestContext.Current.CancellationToken);
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }

        return app;
    }

    public static Uri BaseAddress(WebApplication app) => new(app.Urls.First());
}
