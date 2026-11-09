using Notifications.Application.DependencyInjection;
using Notifications.Infrastructure.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);

// Hosted services (BackgroundService implementations) are registered alongside the layers below.
builder.Services
    .AddNotificationsApplication()
    .AddNotificationsInfrastructure(builder.Configuration);

var host = builder.Build();

await host.RunAsync();
