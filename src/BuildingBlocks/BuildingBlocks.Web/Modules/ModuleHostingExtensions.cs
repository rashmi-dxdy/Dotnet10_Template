using BuildingBlocks.Abstractions.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Web.Modules;

/// <summary>
/// Composes modules into one host. The host lists every module it can run; configuration decides which ones run
/// in this process (<c>Modules:&lt;Name&gt;:Enabled</c>), so the same application runs as a modular monolith (all
/// modules) or as one extracted module (one enabled) without code changes. See docs/modules.md.
/// </summary>
public static class ModuleHostingExtensions
{
    /// <summary>
    /// Registers the services of every enabled module (<see cref="IModule.AddServices"/>), the
    /// <see cref="IModuleHost"/> that module clients use to reach modules in this process, and the internal API key.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="modules">Every module this host can run.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// A module name is invalid or duplicated, no module is enabled, or the internal API settings are invalid.
    /// </exception>
    public static IHostApplicationBuilder AddModules(this IHostApplicationBuilder builder, params IModule[] modules)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(modules);

        if (builder.Services.Any(d => d.ServiceType == typeof(ModuleCatalog)))
        {
            throw new InvalidOperationException("AddModules must be called once, with every module.");
        }

        var catalog = ModuleCatalog.Create(modules, builder.Configuration);

        builder.Services.AddInternalApiKey(builder.Configuration);
        builder.Services.AddSingleton(catalog);
        builder.Services.AddSingleton<IModuleHost>(
            serviceProvider => new WebModuleHost(catalog, serviceProvider.GetRequiredService<IServer>()));

        foreach (var module in catalog.Hosted)
        {
            module.AddServices(builder.Services, builder.Configuration);
        }

        return builder;
    }

    /// <summary>
    /// Maps the public (<c>/api/&lt;module&gt;</c>) and internal (<c>/internal/&lt;module&gt;</c>) endpoints of every
    /// enabled module, and requires the internal API key on every <c>/internal</c> request. Call it after
    /// <c>UseWebServiceDefaults()</c>.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <returns>The same application, for chaining.</returns>
    /// <exception cref="InvalidOperationException"><see cref="AddModules"/> was not called.</exception>
    public static WebApplication MapModules(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var catalog = app.Services.GetService<ModuleCatalog>()
            ?? throw new InvalidOperationException(
                $"Call builder.{nameof(AddModules)}(...) before {nameof(MapModules)}.");
        var key = app.Services.GetRequiredService<InternalApiKey>();

        app.UseMiddleware<InternalApiAccessMiddleware>();

        foreach (var module in catalog.Hosted)
        {
            var logging = new ModuleLoggingFilter(module.Name);

            var publicRoutes = app.MapGroup(ModuleRoutes.Public(module.Name))
                .WithTags(module.Name)
                .AddEndpointFilter(logging);

            var internalRoutes = app.MapGroup(ModuleRoutes.Internal(module.Name))
                .ExcludeFromDescription()
                .AddEndpointFilter(logging);

            module.MapEndpoints(new ModuleRouteGroups(publicRoutes, internalRoutes));
        }

        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ModuleHostingExtensions));
        var hosted = string.Join(", ", catalog.HostedNames);
        var keyDescription = key.ToString();
        ModuleLog.Hosting(logger, hosted, keyDescription);
        if (catalog.NotHosted.Count > 0)
        {
            var notHosted = string.Join(", ", catalog.NotHosted);
            ModuleLog.NotHosting(logger, notHosted);
        }

        return app;
    }

    /// <summary>Binds and validates <c>InternalApi</c> and registers the <see cref="InternalApiKey"/> once.</summary>
    private static void AddInternalApiKey(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(InternalApiOptions.SectionName);
        var problems = (section.Get<InternalApiOptions>() ?? new InternalApiOptions()).Validate();
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "The internal API is not configured correctly: " + string.Join("; ", problems) + ". The key is a "
                + "secret: use user secrets (development) or an environment variable. See docs/modules.md.");
        }

        services.AddOptions<InternalApiOptions>().Bind(section);
        services.TryAddSingleton(
            serviceProvider => new InternalApiKey(
                serviceProvider.GetRequiredService<IOptions<InternalApiOptions>>().Value.ApiKey));
    }
}
