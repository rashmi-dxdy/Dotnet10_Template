using BuildingBlocks.Hosting.Lifetime;
using BuildingBlocks.Hosting.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace BuildingBlocks.Hosting.DependencyInjection;

/// <summary>
/// Host setup shared by every API and Worker.
/// </summary>
public static class HostingBuilderExtensions
{
    /// <summary>Configuration section bound to <see cref="HostOptions"/> (e.g. <c>ShutdownTimeout</c>).</summary>
    public const string HostOptionsSection = "HostOptions";

    /// <summary>
    /// Applies the shared host defaults: Serilog structured logging and graceful-shutdown settings and logging.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddServiceLogging();

        builder.Services.Configure<HostOptions>(builder.Configuration.GetSection(HostOptionsSection));
        builder.Services.AddHostedService<HostLifetimeLogger>();

        return builder;
    }

    /// <summary>
    /// Replaces the default logging providers with Serilog. Levels, sinks and output format are read from the
    /// <c>Serilog</c> configuration section. The standard properties (<see cref="LogProperties"/>, machine
    /// name, thread ID, log context) and sensitive-data redaction are always added in code, so no host
    /// can drop them by accident.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IHostApplicationBuilder AddServiceLogging(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var environment = builder.Environment;
        var configuration = builder.Configuration;

        builder.Services.AddSerilog((services, logger) => logger
            .ReadFrom.Configuration(configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            // Added only if absent, so "Serilog:Properties:Application" in configuration can override them.
            .Enrich.WithProperty(LogProperties.Application, environment.ApplicationName)
            .Enrich.WithProperty(LogProperties.Environment, environment.EnvironmentName)
            .Enrich.WithMachineName()
            .Enrich.WithThreadId()
            // Last, so it also sees the properties added by the enrichers above.
            .Enrich.With<SensitiveDataRedactionEnricher>());

        return builder;
    }
}
