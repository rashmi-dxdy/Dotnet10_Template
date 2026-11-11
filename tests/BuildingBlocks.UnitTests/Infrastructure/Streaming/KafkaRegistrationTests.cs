using BuildingBlocks.Abstractions.Health;
using BuildingBlocks.Abstractions.Streaming;
using BuildingBlocks.Infrastructure.DependencyInjection;
using BuildingBlocks.Infrastructure.Streaming.Kafka;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.UnitTests.Infrastructure.Streaming;

public sealed class KafkaRegistrationTests
{
    private const string Password = "s3cr3t-value";

    [Fact]
    public void AddKafkaStreaming_WithDefaults_NeedsNoCredentials()
    {
        using var provider = BuildProvider();

        var options = provider.GetRequiredService<IOptions<KafkaOptions>>().Value;

        Assert.Equal("localhost:9092", options.BootstrapServers);
        Assert.Equal(SecurityProtocol.Plaintext, options.SecurityProtocol);
        Assert.Equal(Acks.All, options.Producer.Acks);
        Assert.True(options.Producer.EnableIdempotence);
    }

    [Fact]
    public void AddKafkaStreaming_WithSaslButNoCredentials_FailsWithInstructions_WithoutThePassword()
    {
        var configuration = Configuration(new(StringComparer.Ordinal)
        {
            ["Kafka:SecurityProtocol"] = "SaslSsl",
            ["Kafka:SaslMechanism"] = "ScramSha512",
            ["Kafka:Consumer:RetryBackoffMs"] = "0",
        });

        var exception = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddKafkaStreaming(configuration));

        Assert.Contains(
            "Kafka:SaslUsername is required for SaslSsl (environment variable Kafka__SaslUsername)",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains("Kafka:SaslPassword is required for SaslSsl", exception.Message, StringComparison.Ordinal);
        Assert.Contains(
            "Kafka:Consumer:RetryBackoffMs must be positive (environment variable Kafka__Consumer__RetryBackoffMs)",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AddKafkaStreaming_BindsSecurityAndTopicSettings_AndTargetHidesThePassword()
    {
        using var provider = BuildProvider(new(StringComparer.Ordinal)
        {
            ["Kafka:BootstrapServers"] = "kafka-1:9093,kafka-2:9093",
            ["Kafka:SecurityProtocol"] = "SaslSsl",
            ["Kafka:SaslMechanism"] = "ScramSha512",
            ["Kafka:SaslUsername"] = "svc",
            ["Kafka:SaslPassword"] = Password,
            ["Kafka:Topics:0:Name"] = "sample.events",
            ["Kafka:Topics:0:Partitions"] = "6",
        });

        var options = provider.GetRequiredService<IOptions<KafkaOptions>>().Value;
        var target = KafkaClientConfiguration.Target(options);

        Assert.Equal("sample.events", Assert.Single(options.Topics).Name);
        Assert.Equal(6, options.Topics[0].Partitions);
        Assert.Equal("kafka-1:9093,kafka-2:9093 (SaslSsl, ScramSha512, user svc)", target);
        Assert.DoesNotContain(Password, target, StringComparison.Ordinal);
    }

    [Fact]
    public void ClientConfigurations_UseReliableProducerAndManualOffsetStore()
    {
        var options = new KafkaOptions { Consumer = { GroupId = "sample" } };

        var producer = KafkaClientConfiguration.Producer(options);
        var consumer = KafkaClientConfiguration.Consumer(options, "sample");

        Assert.Equal(Acks.All, producer.Acks);
        Assert.True(producer.EnableIdempotence);
        Assert.Equal("sample", consumer.GroupId);
        Assert.True(consumer.EnableAutoCommit);
        Assert.False(consumer.EnableAutoOffsetStore);
        Assert.Equal(AutoOffsetReset.Earliest, consumer.AutoOffsetReset);
    }

    [Fact]
    public void AddKafkaStreaming_RegistersOneProducer_Lifetime_AndReadyHealthCheck()
    {
        using var provider = BuildProvider();

        Assert.Same(provider.GetRequiredService<KafkaProducer>(), provider.GetRequiredService<IEventStreamProducer>());
        Assert.Contains(provider.GetServices<IHostedService>(), s => s is KafkaClientsLifetime);

        var registration = Assert.Single(
            provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations,
            r => string.Equals(r.Name, "kafka", StringComparison.Ordinal));
        Assert.Contains(HealthCheckTags.Ready, registration.Tags);
    }

    [Fact]
    public void AddKafkaConsumer_IsRegisteredAfterTheClientsLifetime_SoItStopsFirst()
    {
        var services = Services();
        services.AddKafkaConsumer<SampleMessage, SampleHandler>("sample.events");
        using var provider = services.BuildServiceProvider();

        var hostedServices = provider.GetServices<IHostedService>().ToList();
        var lifetime = hostedServices.FindIndex(s => s is KafkaClientsLifetime);
        var consumer = hostedServices.FindIndex(s => s is KafkaConsumer<SampleMessage, SampleHandler>);

        Assert.True(lifetime >= 0 && consumer > lifetime);
    }

    [Fact]
    public void AddKafkaConsumer_BeforeAddKafkaStreaming_Fails() =>
        Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddKafkaConsumer<SampleMessage, SampleHandler>("sample.events"));

    [Theory]
    [InlineData(null, "sample.events.notifications.dlt")]
    [InlineData("projection", "sample.events.projection.dlt")]
    public void DeadLetterTopic_IsPerTopicAndConsumerGroup(string? groupOverride, string expected) =>
        Assert.Equal(expected, new KafkaSubscription("sample.events", groupOverride).DeadLetterTopic("notifications"));

    [Fact]
    public async Task HealthCheck_WhenBrokerIsUnreachable_IsUnhealthyWithAClearReason()
    {
        await using var provider = BuildProvider(new(StringComparer.Ordinal)
        {
            ["Kafka:BootstrapServers"] = "127.0.0.1:1",
            ["Kafka:AdminTimeoutSeconds"] = "1",
        });

        var report = await provider.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(TestContext.Current.CancellationToken);

        var entry = report.Entries["kafka"];
        Assert.Equal(HealthStatus.Unhealthy, entry.Status);
        Assert.Contains("Kafka 127.0.0.1:1 (Plaintext) is unavailable", entry.Description, StringComparison.Ordinal);
        Assert.Contains("Is Kafka running", entry.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GracefulShutdown_WithAConsumer_CompletesPromptly_EvenWhenKafkaIsUnreachable()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Kafka:BootstrapServers"] = "127.0.0.1:1",
            ["Kafka:AdminTimeoutSeconds"] = "1",
            ["Kafka:Consumer:GroupId"] = "shutdown-test",
        });
        builder.Services.AddKafkaStreaming(builder.Configuration);
        builder.Services.AddKafkaConsumer<SampleMessage, SampleHandler>("sample.events");
        using var host = builder.Build();

        await host.StartAsync(TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await host.StopAsync(TestContext.Current.CancellationToken);

        // Consumer Close() and the producer flush must not hang on an unreachable cluster.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Shutdown took {stopwatch.Elapsed}.");
    }

    private static ServiceCollection Services(Dictionary<string, string?>? settings = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKafkaStreaming(Configuration(settings ?? []));
        return services;
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?>? settings = null) =>
        Services(settings).BuildServiceProvider();

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    internal sealed record SampleMessage(string Text);

    internal sealed class SampleHandler : IStreamMessageHandler<SampleMessage>
    {
        public Task HandleAsync(StreamMessageContext<SampleMessage> context, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
