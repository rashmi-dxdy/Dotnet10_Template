using BuildingBlocks.Abstractions.Health;
using BuildingBlocks.Abstractions.Messaging;
using BuildingBlocks.Infrastructure.Messaging.RabbitMq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace BuildingBlocks.Infrastructure.DependencyInjection;

/// <summary>
/// Central RabbitMQ setup. Services call <see cref="AddRabbitMqMessaging"/> once, then
/// <see cref="AddRabbitMqConsumer{TMessage, THandler}"/> per queue they consume.
/// </summary>
public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the shared RabbitMQ connection (opened in the background with retries, closed on shutdown),
    /// <see cref="IMessagePublisher"/> (persistent messages, publisher confirms) and a RabbitMQ health check
    /// (tag <c>ready</c>). Settings come from the <c>RabbitMq</c> section / <c>RabbitMq__*</c> environment
    /// variables.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <exception cref="InvalidOperationException">The RabbitMQ settings are missing or invalid.</exception>
    public static IServiceCollection AddRabbitMqMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        if (services.Any(d => d.ServiceType == typeof(RabbitMqConnection)))
        {
            return services;
        }

        var section = configuration.GetSection(RabbitMqOptions.SectionName);

        // Fail at startup, with instructions, rather than on the first message.
        var problems = (section.Get<RabbitMqOptions>() ?? new RabbitMqOptions()).Validate();
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "RabbitMQ is not configured correctly: " + string.Join("; ", problems) + ". Credentials are never "
                + "stored in appsettings*.json. Development: `dotnet user-secrets set \"RabbitMq:Password\" \"...\"` "
                + "in the host project folder. Docker/CI: environment variables. See docs/messaging.md.");
        }

        services.AddOptions<RabbitMqOptions>().Bind(section);
        services.AddSingleton<RabbitMqConnection>();
        services.AddSingleton<IMessagePublisher, RabbitMqPublisher>();

        // Registered before any consumer, so the host stops it after them (hosted services stop in reverse order).
        services.AddHostedService<RabbitMqConnectionLifetime>();

        services.AddHealthChecks().Add(new HealthCheckRegistration(
            "rabbitmq",
            serviceProvider => new RabbitMqHealthCheck(serviceProvider.GetRequiredService<RabbitMqConnection>()),
            HealthStatus.Unhealthy,
            [HealthCheckTags.Ready]));

        return services;
    }

    /// <summary>
    /// Consumes <paramref name="queue"/> (a durable quorum queue with a dead-letter queue, declared if missing)
    /// bound to <paramref name="topics"/>, passing each message to <typeparamref name="THandler"/>.
    /// Call <see cref="AddRabbitMqMessaging"/> first.
    /// </summary>
    /// <typeparam name="TMessage">The message type.</typeparam>
    /// <typeparam name="THandler">The handler, registered as scoped.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="queue">The queue, named <c>&lt;consuming-service&gt;.&lt;purpose&gt;</c>, e.g. <c>notifications.order-placed</c>.</param>
    /// <param name="topics">The topics to bind, e.g. <c>orders.order-placed</c>. <c>*</c> and <c>#</c> wildcards allowed.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddRabbitMqConsumer<TMessage, THandler>(
        this IServiceCollection services,
        string queue,
        params string[] topics)
        where TMessage : class
        where THandler : class, IMessageHandler<TMessage>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(queue);
        ArgumentNullException.ThrowIfNull(topics);
        if (topics.Length == 0 || topics.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("At least one non-empty topic is required.", nameof(topics));
        }

        if (!services.Any(d => d.ServiceType == typeof(RabbitMqConnection)))
        {
            throw new InvalidOperationException(
                $"Call {nameof(AddRabbitMqMessaging)} before {nameof(AddRabbitMqConsumer)}: the connection must be "
                + "registered first so it is closed after the consumers on shutdown.");
        }

        services.TryAddScoped<THandler>();
        var subscription = new RabbitMqSubscription(queue, topics);

        // AddSingleton, not AddHostedService: one hosted consumer per queue, even for the same handler type.
        services.AddSingleton<IHostedService>(serviceProvider =>
            ActivatorUtilities.CreateInstance<RabbitMqConsumer<TMessage, THandler>>(serviceProvider, subscription));

        return services;
    }
}
