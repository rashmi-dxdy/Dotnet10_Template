using BuildingBlocks.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Catalog.Infrastructure.DependencyInjection;

/// <summary>
/// Registration entry point for the Catalog service messaging (RabbitMQ).
/// </summary>
public static class MessagingServiceCollectionExtensions
{
    /// <summary>Queue name prefix for this service: every queue it consumes is named <c>catalog.&lt;purpose&gt;</c>.</summary>
    public const string QueuePrefix = "catalog.";

    /// <summary>
    /// Registers the shared RabbitMQ connection, <c>IMessagePublisher</c> and the RabbitMQ health check, plus this
    /// service consumers.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddCatalogMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddRabbitMqMessaging(configuration);

        // Extension point: register one consumer per queue with AddRabbitMqConsumer, naming the queue
        // QueuePrefix + purpose and binding the publishing service topics. Handlers live in Infrastructure/Messaging.
        // See docs/messaging.md.
        return services;
    }
}
