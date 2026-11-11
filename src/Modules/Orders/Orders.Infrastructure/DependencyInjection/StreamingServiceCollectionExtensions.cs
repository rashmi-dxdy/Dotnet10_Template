using BuildingBlocks.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Orders.Infrastructure.DependencyInjection;

/// <summary>
/// Registration entry point for the Orders module event streaming (Kafka), independent of its RabbitMQ messaging.
/// </summary>
public static class StreamingServiceCollectionExtensions
{
    /// <summary>
    /// The Kafka consumer group of this module, equal to the module name. Pass it to every
    /// <c>AddKafkaConsumer</c> call so the module keeps its own offsets: inside the monolith (where all modules share
    /// one process) and unchanged after the module is extracted.
    /// </summary>
    public const string ConsumerGroup = "orders";

    /// <summary>
    /// Registers the Kafka producer (<c>IEventStreamProducer</c>), topic provisioning and the Kafka health check,
    /// plus this module's stream consumers (consumer group <see cref="ConsumerGroup"/>).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddOrdersStreaming(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddKafkaStreaming(configuration);

        // Extension point: register one consumer per consumed topic with
        // AddKafkaConsumer<TMessage, THandler>(topic, ConsumerGroup), handlers in Infrastructure/Messaging.
        // Topics this module produces to go in Kafka:Topics. See docs/streaming.md.
        return services;
    }
}
