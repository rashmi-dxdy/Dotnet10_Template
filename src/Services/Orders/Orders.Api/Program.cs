using BuildingBlocks.Hosting;
using BuildingBlocks.Web.DependencyInjection;
using Orders.Api.ExceptionHandling;
using Orders.Application.DependencyInjection;
using Orders.Infrastructure.DependencyInjection;

return await ServiceHost.RunAsync(async () =>
{
    var builder = WebApplication.CreateBuilder(args);

    // Serilog structured logging, problem details and graceful shutdown (see docs/logging.md).
    builder.AddWebServiceDefaults();

    builder.Services
        .AddOrdersApplication()
        .AddOrdersInfrastructure(builder.Configuration);

    builder.Services.AddExceptionHandler<ValidationExceptionHandler>();
    builder.Services.AddOpenApi();
    builder.Services.AddHealthChecks();

    await using var app = builder.Build();

    // Correlation ID, request logging and exception handling - must come first.
    app.UseWebServiceDefaults();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    app.MapHealthChecks("/health");

    await app.RunAsync();
});
