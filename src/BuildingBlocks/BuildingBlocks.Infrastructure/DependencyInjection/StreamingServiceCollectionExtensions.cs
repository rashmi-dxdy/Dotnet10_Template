using BuildingBlocks.Abstractions.Health;
using BuildingBlocks.Abstractions.Streaming;
using BuildingBlocks.Infrastructure.Streaming.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace BuildingBlocks.Infrastructure.DependencyInjection;

/// <summary>
/// Central Kafka setup, independent of RabbitMQ. Services call <see cref="AddKafkaStreaming"/> once, then
/// <see cref="AddKafkaConsumer{TMessage, THandler}"/> per topic they consume.
/// </summary>
public static class StreamingServiceCollectionExtensions
{
    /// <summary>
    /// Registers one reliable Kafka producer (<see cref="IEventStreamProducer"/>), one admin client, the startup
    /// check and topic provisioning, producer flushing on shutdown, and a Kafka health check (tag <c>ready</c>).
    /// Settings come from the <c>Kafka</c> section / <c>Kafka__*</c> environment variables.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <exception cref="InvalidOperationException">The Kafka settings are invalid.</exception>
    public static IServiceCollection AddKafkaStreaming(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        if (services.Any(d => d.ServiceType == typeof(KafkaProducer)))
        {
            return services;
        }

        var section = configuration.GetSection(KafkaOptions.SectionName);
        var problems = (section.Get<KafkaOptions>() ?? new KafkaOptions()).Validate();
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "Kafka is not configured correctly: " + string.Join("; ", problems) + ". Credentials are never "
                + "stored in appsettings*.json: use user secrets (development) or environment variables. "
                + "See docs/streaming.md.");
        }

        services.AddOptions<KafkaOptions>().Bind(section);
        services.AddSingleton<KafkaAdmin>();
        services.AddSingleton<KafkaProducer>();
        services.AddSingleton<IEventStreamProducer>(
            serviceProvider => serviceProvider.GetRequiredService<KafkaProducer>());

        // Registered before any consumer, so the host stops it after them and the producer flush comes last.
        services.AddHostedService<KafkaClientsLifetime>();

        services.AddHealthChecks().Add(new HealthCheckRegistration(
            "kafka",
            serviceProvider => new KafkaHealthCheck(serviceProvider.GetRequiredService<KafkaAdmin>()),
            HealthStatus.Unhealthy,
            [HealthCheckTags.Ready]));

        return services;
    }

    /// <summary>
    /// Consumes <paramref name="topic"/> as consumer group <paramref name="groupId"/> (default
    /// <c>Kafka:Consumer:GroupId</c>), passing each message to <typeparamref name="THandler"/>. Failed messages go
    /// to <c>&lt;topic&gt;.&lt;group&gt;.dlt</c>. Call <see cref="AddKafkaStreaming"/> first.
    /// </summary>
    /// <typeparam name="TMessage">The message type.</typeparam>
    /// <typeparam name="THandler">The handler, registered as scoped.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="topic">The topic, e.g. <c>orders.order-events</c>.</param>
    /// <param name="groupId">
    /// The consumer group, only when this consumer must track its offsets separately from the service's other
    /// consumers (e.g. a projection that can be rebuilt independently).
    /// </param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddKafkaConsumer<TMessage, THandler>(
        this IServiceCollection services,
        string topic,
        string? groupId = null)
        where TMessage : class
        where THandler : class, IStreamMessageHandler<TMessage>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        if (!services.Any(d => d.ServiceType == typeof(KafkaProducer)))
        {
            throw new InvalidOperationException(
                $"Call {nameof(AddKafkaStreaming)} before {nameof(AddKafkaConsumer)}: the producer must be registered "
                + "first so it is flushed after the consumers on shutdown.");
        }

        var subscription = new KafkaSubscription(topic, groupId);
        services.TryAddScoped<THandler>();
        services.AddSingleton(subscription);

        // AddSingleton, not AddHostedService: one hosted consumer per subscription, even for the same handler type.
        services.AddSingleton<IHostedService>(serviceProvider =>
            ActivatorUtilities.CreateInstance<KafkaConsumer<TMessage, THandler>>(serviceProvider, subscription));

        return services;
    }
}
