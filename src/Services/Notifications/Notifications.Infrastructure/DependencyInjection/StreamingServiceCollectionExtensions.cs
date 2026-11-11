using BuildingBlocks.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Notifications.Infrastructure.DependencyInjection;

/// <summary>
/// Registration entry point for the Notifications service event streaming (Kafka), independent of its RabbitMQ messaging.
/// </summary>
public static class StreamingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Kafka producer (<c>IEventStreamProducer</c>), topic provisioning and the Kafka health check,
    /// plus this service stream consumers. The consumer group comes from <c>Kafka:Consumer:GroupId</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddNotificationsStreaming(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddKafkaStreaming(configuration);

        // Extension point: register one consumer per consumed topic with AddKafkaConsumer, handlers in
        // Infrastructure/Messaging. Topics this service produces to go in Kafka:Topics. See docs/streaming.md.
        return services;
    }
}
