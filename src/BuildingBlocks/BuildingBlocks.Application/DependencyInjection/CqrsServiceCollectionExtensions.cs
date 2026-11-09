using System.Reflection;
using BuildingBlocks.Application.Behaviors;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Application.DependencyInjection;

/// <summary>
/// Registers MediatR as the CQRS dispatcher with FluentValidation and the shared pipeline behaviors.
/// </summary>
public static class CqrsServiceCollectionExtensions
{
    /// <summary>
    /// Registers MediatR, every command/query handler and FluentValidation validator found in
    /// <paramref name="applicationAssembly"/>, and the shared pipeline behaviors. Call once per service
    /// from its <c>Add&lt;Service&gt;Application()</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="applicationAssembly">The service's Application assembly.</param>
    /// <param name="configure">Optional extra configuration, e.g. service-specific behaviors.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddCqrs(
        this IServiceCollection services,
        Assembly applicationAssembly,
        Action<MediatRServiceConfiguration>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(applicationAssembly);

        services.AddValidatorsFromAssembly(applicationAssembly, ServiceLifetime.Scoped, includeInternalTypes: true);

        return services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(applicationAssembly);

            // Behaviors run in registration order: the first one registered is the outermost.
            // Logging wraps validation, so rejected requests are logged too.
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));

            configure?.Invoke(cfg);
        });
    }
}
