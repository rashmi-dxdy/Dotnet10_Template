using BuildingBlocks.Hosting;
using BuildingBlocks.Web.DependencyInjection;
using BuildingBlocks.Web.Health;
using BuildingBlocks.Web.Modules;
using Catalog.Api;
using Notifications.Api;
using Orders.Api;

return await ServiceHost.RunAsync(async () =>
{
    var builder = WebApplication.CreateBuilder(args);

    // Serilog structured logging, problem details (incl. validation failures) and graceful shutdown.
    builder.AddWebServiceDefaults();

    // Every module this application can run. Which ones run in THIS process is configuration
    // (Modules:<Name>:Enabled), so a module is extracted without code changes (see docs/modules.md).
    builder.AddModules(
        new CatalogModule(),
        new OrdersModule(),
        new NotificationsModule());

    builder.Services.AddOpenApi();

    await using var app = builder.Build();

    // Correlation ID, request logging and exception handling - must come first.
    app.UseWebServiceDefaults();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    // /health/live (process up) and /health/ready (every enabled module's dependencies reachable).
    app.MapServiceHealthChecks();

    // /api/<module>/... (public) and /internal/<module>/... (other modules only, internal API key required).
    app.MapModules();

    await app.RunAsync();
});
