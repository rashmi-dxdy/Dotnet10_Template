using BuildingBlocks.Hosting;
using BuildingBlocks.Hosting.DependencyInjection;
using Notifications.Application.DependencyInjection;
using Notifications.Infrastructure.DependencyInjection;

return await ServiceHost.RunAsync(async () =>
{
    var builder = Host.CreateApplicationBuilder(args);

    // Serilog structured logging and graceful shutdown (see docs/logging.md).
    builder.AddServiceDefaults();

    // Hosted services (BackgroundService implementations) are registered alongside the layers below.
    builder.Services
        .AddNotificationsApplication()
        .AddNotificationsInfrastructure(builder.Configuration);

    using var host = builder.Build();

    await host.RunAsync();
});
