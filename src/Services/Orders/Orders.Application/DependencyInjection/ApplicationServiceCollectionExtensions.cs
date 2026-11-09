using Microsoft.Extensions.DependencyInjection;

namespace Orders.Application.DependencyInjection;

/// <summary>
/// Registration entry point for the Orders application layer.
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers Orders application services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddOrdersApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Extension points, in registration order:
        //   Commands/, Queries/  -> handlers as ICommandHandler<,> / IQueryHandler<,> (scoped).
        //   Validators/          -> command and query validators.
        //   Behaviors/           -> decorators around the handlers registered above.
        return services;
    }
}
