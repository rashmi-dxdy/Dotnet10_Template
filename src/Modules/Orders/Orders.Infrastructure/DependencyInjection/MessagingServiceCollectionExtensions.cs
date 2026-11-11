using BuildingBlocks.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Orders.Infrastructure.DependencyInjection;

/// <summary>
/// Registration entry point for the Orders module messaging (RabbitMQ).
/// </summary>
public static class MessagingServiceCollectionExtensions
{
    /// <summary>Queue name prefix for this module: every queue it consumes is named <c>orders.&lt;purpose&gt;</c>.</summary>
    public const string QueuePrefix = "orders.";

    /// <summary>
    /// Registers the shared RabbitMQ connection, <c>IMessagePublisher</c> and the RabbitMQ health check, plus this
    /// module consumers.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddOrdersMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddRabbitMqMessaging(configuration);

        // Extension point: register one consumer per queue with AddRabbitMqConsumer, naming the queue
        // QueuePrefix + purpose and binding the publishing module topics. Handlers live in Infrastructure/Messaging.
        // See docs/messaging.md.
        return services;
    }
}
