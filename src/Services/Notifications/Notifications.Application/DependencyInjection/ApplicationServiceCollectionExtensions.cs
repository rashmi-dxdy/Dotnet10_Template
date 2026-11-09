using BuildingBlocks.Application.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Notifications.Application.DependencyInjection;

/// <summary>
/// Registration entry point for the Notifications application layer.
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers Notifications application services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddNotificationsApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // MediatR + FluentValidation: registers ISender/IMediator, every handler in Commands/ and
        // Queries/, every validator in Validators/, and the shared pipeline behaviors (logging, validation).
        // Add service-specific behaviors from Behaviors/ via the configure callback:
        //   AddCqrs(assembly, cfg => cfg.AddOpenBehavior(typeof(MyBehavior<,>))).
        services.AddCqrs(typeof(ApplicationServiceCollectionExtensions).Assembly);

        return services;
    }
}
