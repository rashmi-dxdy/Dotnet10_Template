using BuildingBlocks.Hosting;
using BuildingBlocks.Web.DependencyInjection;
using BuildingBlocks.Web.Health;
using Catalog.Api.ExceptionHandling;
using Catalog.Application.DependencyInjection;
using Catalog.Infrastructure.DependencyInjection;

return await ServiceHost.RunAsync(async () =>
{
    var builder = WebApplication.CreateBuilder(args);

    // Serilog structured logging, problem details and graceful shutdown (see docs/logging.md).
    builder.AddWebServiceDefaults();

    builder.Services
        .AddCatalogApplication()
        .AddCatalogInfrastructure(builder.Configuration);

    builder.Services.AddExceptionHandler<ValidationExceptionHandler>();
    builder.Services.AddOpenApi();

    await using var app = builder.Build();

    // Correlation ID, request logging and exception handling - must come first.
    app.UseWebServiceDefaults();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    // /health/live (process up) and /health/ready (dependencies such as PostgreSQL reachable).
    app.MapServiceHealthChecks();

    await app.RunAsync();
});
