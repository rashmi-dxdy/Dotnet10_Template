using Notifications.Application;
using Notifications.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddNotificationsApplication()
    .AddNotificationsInfrastructure(builder.Configuration);

// Register hosted services (BackgroundService implementations) here.

var host = builder.Build();

await host.RunAsync();
