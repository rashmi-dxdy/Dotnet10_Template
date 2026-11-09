using BuildingBlocks.Abstractions.Time;
using BuildingBlocks.Infrastructure.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks.Infrastructure.DependencyInjection;

/// <summary>
/// Registration entry point for shared technical infrastructure.
/// </summary>
public static class BuildingBlocksServiceCollectionExtensions
{
    /// <summary>
    /// Registers shared technical services. Extension point: add cross-cutting plumbing here
    /// (for example messaging or persistence helpers), never business services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddBuildingBlocksInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        return services;
    }
}
