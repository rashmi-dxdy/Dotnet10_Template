using BuildingBlocks.Abstractions.Health;
using BuildingBlocks.Abstractions.Messaging;
using BuildingBlocks.Infrastructure.DependencyInjection;
using BuildingBlocks.Infrastructure.Messaging.RabbitMq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.UnitTests.Infrastructure.Messaging;

public sealed class RabbitMqRegistrationTests
{
    private const string Password = "s3cr3t-value";

    [Fact]
    public void AddRabbitMqMessaging_WithoutCredentials_FailsWithInstructions()
    {
        var configuration = Configuration(new(StringComparer.Ordinal) { ["RabbitMq:Host"] = "broker" });

        var exception = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddRabbitMqMessaging(configuration));

        Assert.Contains(
            "RabbitMq:Username is required (environment variable RabbitMq__Username)",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "RabbitMq:Password is required (environment variable RabbitMq__Password)",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains("docs/messaging.md", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddRabbitMqMessaging_WithInvalidSettings_ListsEveryProblem_WithoutThePassword()
    {
        var configuration = Configuration(new(StringComparer.Ordinal)
        {
            ["RabbitMq:Username"] = "app",
            ["RabbitMq:Password"] = Password,
            ["RabbitMq:Port"] = "70000",
            ["RabbitMq:DeliveryLimit"] = "0",
        });

        var exception = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddRabbitMqMessaging(configuration));

        Assert.Contains("RabbitMq:Port must be between 1 and 65535", exception.Message, StringComparison.Ordinal);
        Assert.Contains("RabbitMq:DeliveryLimit must be positive", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddRabbitMqMessaging_RegistersPublisherConnectionLifetimeAndReadyHealthCheck()
    {
        using var provider = BuildProvider();

        Assert.IsType<RabbitMqPublisher>(provider.GetRequiredService<IMessagePublisher>());
        Assert.Contains(provider.GetServices<IHostedService>(), s => s is RabbitMqConnectionLifetime);

        var registration = Assert.Single(
            provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations,
            r => string.Equals(r.Name, "rabbitmq", StringComparison.Ordinal));
        Assert.Contains(HealthCheckTags.Ready, registration.Tags);
    }

    [Fact]
    public void AddRabbitMqMessaging_BindsEnvironmentStyleSettings()
    {
        using var provider = BuildProvider(new(StringComparer.Ordinal)
        {
            ["RabbitMq:Host"] = "rabbitmq",
            ["RabbitMq:Port"] = "5673",
            ["RabbitMq:VirtualHost"] = "template",
        });

        var options = provider.GetRequiredService<IOptions<RabbitMqOptions>>().Value;

        Assert.Equal("app@rabbitmq:5673/template", options.Target);
        Assert.DoesNotContain(Password, options.Target, StringComparison.Ordinal);
    }

    [Fact]
    public void AddRabbitMqConsumer_IsRegisteredAfterTheConnectionLifetime_SoItStopsFirst()
    {
        var services = Services();
        services.AddRabbitMqConsumer<SampleMessage, SampleHandler>("sample.queue", "sample.topic");
        using var provider = services.BuildServiceProvider();

        var hostedServices = provider.GetServices<IHostedService>().ToList();
        var lifetime = hostedServices.FindIndex(s => s is RabbitMqConnectionLifetime);
        var consumer = hostedServices.FindIndex(s => s is RabbitMqConsumer<SampleMessage, SampleHandler>);

        // The host stops hosted services in reverse order: consumers first, then the connection.
        Assert.True(lifetime >= 0 && consumer > lifetime);
        Assert.NotNull(provider.CreateScope().ServiceProvider.GetService<SampleHandler>());
    }

    [Fact]
    public void AddRabbitMqConsumer_BeforeAddRabbitMqMessaging_Fails()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddRabbitMqConsumer<SampleMessage, SampleHandler>("q", "t"));

        Assert.Contains(
            nameof(MessagingServiceCollectionExtensions.AddRabbitMqMessaging),
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task HealthCheck_BeforeConnecting_IsUnhealthy_WithTheSafeTarget()
    {
        await using var provider = BuildProvider();

        var report = await provider.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(TestContext.Current.CancellationToken);

        var entry = report.Entries["rabbitmq"];
        Assert.Equal(HealthStatus.Unhealthy, entry.Status);
        Assert.Contains("RabbitMQ app@localhost:5672/ is not connected", entry.Description, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, entry.Description, StringComparison.Ordinal);
    }

    private static ServiceCollection Services(Dictionary<string, string?>? settings = null)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["RabbitMq:Username"] = "app",
            ["RabbitMq:Password"] = Password,
        };
        foreach (var (key, value) in settings ?? [])
        {
            values[key] = value;
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRabbitMqMessaging(Configuration(values));
        return services;
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?>? settings = null) =>
        Services(settings).BuildServiceProvider();

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    internal sealed record SampleMessage(string Text);

    internal sealed class SampleHandler : IMessageHandler<SampleMessage>
    {
        public Task<MessageHandlingResult> HandleAsync(
            MessageContext<SampleMessage> context,
            CancellationToken cancellationToken) =>
            Task.FromResult(MessageHandlingResult.Ack);
    }
}
