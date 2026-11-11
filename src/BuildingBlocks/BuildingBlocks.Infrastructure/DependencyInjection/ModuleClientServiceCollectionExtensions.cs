using BuildingBlocks.Abstractions.Correlation;
using BuildingBlocks.Abstractions.Modules;
using BuildingBlocks.Infrastructure.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Infrastructure.DependencyInjection;

/// <summary>
/// Clients for calling another module's internal API (<c>/internal/&lt;module&gt;/...</c>) over HTTP. This is the
/// only way modules talk to each other synchronously: never through another module's types or tables. Calls go
/// over HTTP even when both modules run in the same process, so nothing changes when a module is extracted.
/// See docs/modules.md.
/// </summary>
public static class ModuleClientServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TImplementation"/> as a typed <see cref="HttpClient"/> client of
    /// <paramref name="module"/>'s internal API. Its <see cref="HttpClient.BaseAddress"/> ends with
    /// <c>/internal/&lt;module&gt;/</c>, so use relative paths without a leading slash, e.g.
    /// <c>GetAsync($"products/{id}")</c>. Every request:
    /// <list type="bullet">
    ///   <item>goes to <c>Modules:&lt;Module&gt;:BaseAddress</c> if set, otherwise to this process (the module must
    ///   then be enabled here; checked at startup);</item>
    ///   <item>carries the internal API key and the current correlation ID;</item>
    ///   <item>runs through the standard resilience pipeline (timeouts, circuit breaker; retries only for safe
    ///   methods such as GET, so a command is never sent twice);</item>
    ///   <item>throws <see cref="ModuleCallException"/> when the module cannot be reached. Use
    ///   <see cref="ModuleHttpResponseExtensions.EnsureModuleSuccessAsync"/> for error responses.</item>
    /// </list>
    /// </summary>
    /// <typeparam name="TClient">The port the caller's Application layer defines, e.g. <c>ICatalogGateway</c>.</typeparam>
    /// <typeparam name="TImplementation">The HTTP implementation, in the caller's Infrastructure layer.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="module">The called module, e.g. <c>CatalogModuleApi.Name</c>.</param>
    /// <returns>The HTTP client builder, to customize the client further.</returns>
    /// <exception cref="InvalidOperationException">The module's base address or the internal API key is invalid.</exception>
    public static IHttpClientBuilder AddModuleClient<TClient, TImplementation>(
        this IServiceCollection services,
        IConfiguration configuration,
        string module)
        where TClient : class
        where TImplementation : class, TClient
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        if (!ModuleRoutes.IsValidName(module))
        {
            throw new ArgumentException(
                $"'{module}' is not a valid module name: lowercase letters, digits and hyphens.",
                nameof(module));
        }

        var registration = new ModuleClientRegistration(module, BaseAddress(configuration, module), typeof(TClient));
        services.AddSingleton(registration);
        services.AddModuleClientInfrastructure(configuration);

        var builder = services.AddHttpClient<TClient, TImplementation>(
            client => client.BaseAddress = ModuleClientRegistration.PlaceholderBaseAddress(module));

        // Outermost first: error translation, then resilience, then (per attempt) addressing and headers.
        builder.AddHttpMessageHandler(() => new ModuleCallErrorHandler(module));
        builder.AddStandardResilienceHandler(options => options.Retry.DisableForUnsafeHttpMethods());
        builder.AddHttpMessageHandler(serviceProvider => new ModuleClientHandler(
            module,
            serviceProvider.GetRequiredService<ModuleAddressResolver>(),
            serviceProvider.GetRequiredService<InternalApiKey>(),
            serviceProvider.GetService<ICorrelationIdAccessor>()));

        return builder;
    }

    /// <summary>Registers what every module client shares, once.</summary>
    private static void AddModuleClientInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(d => d.ServiceType == typeof(ModuleAddressResolver)))
        {
            return;
        }

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
        services.AddSingleton<ModuleAddressResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ModuleClientsStartupCheck>());
    }

    private static Uri? BaseAddress(IConfiguration configuration, string module)
    {
        var value = configuration.GetSection(ModuleSettings.SectionFor(module)).Get<ModuleSettings>()?.BaseAddress;
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
                || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)))
        {
            return uri;
        }

        throw new InvalidOperationException(
            $"{ModuleSettings.SectionFor(module)}:BaseAddress must be an absolute http or https URL of the "
            + $"process hosting '{module}' (environment variable "
            + $"{ModuleSettings.SectionName}__{module}__BaseAddress).");
    }
}
