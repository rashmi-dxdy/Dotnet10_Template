using System.Globalization;
using BuildingBlocks.Abstractions.Streaming;
using BuildingBlocks.Infrastructure.DependencyInjection;
using BuildingBlocks.Infrastructure.Streaming.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.IntegrationTests.Streaming;

/// <summary>
/// Infrastructure smoke tests for the Kafka adapter against a real cluster: produce, consume, per-key ordering,
/// committed offsets across a restart, retries then dead-letter topic, health check, cancellation and graceful
/// shutdown. Uses a generic test event only. Set <c>Kafka__BootstrapServers</c> (and the security settings if
/// needed) to run them; otherwise every test is skipped.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KafkaSmokeTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    // Unique names per test, so runs never interfere and cleanup removes everything this test created.
    private readonly string _topic = $"smoke-test.{Guid.NewGuid():N}";
    private readonly string _groupId = $"smoke-test-{Guid.NewGuid():N}";
    private readonly List<IHost> _hosts = [];
    private KafkaTestCluster? _cluster;

    private string DeadLetterTopic => new KafkaSubscription(_topic, null).DeadLetterTopic(_groupId);

    public async ValueTask InitializeAsync() =>
        _cluster = await KafkaTestCluster.ConnectOrSkipAsync(TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            await host.StopAsync(CancellationToken.None);
            host.Dispose();
        }

        if (_cluster is not null)
        {
            await _cluster.DeleteTopicsAsync(_topic, DeadLetterTopic);
            _cluster.Dispose();
        }
    }

    [Fact]
    public async Task ProducedMessage_IsConsumed_WithKeyHeadersAndPosition()
    {
        var recorder = new StreamRecorder(_ => { });
        var host = await StartHostAsync(recorder);
        var message = SmokeTestEvent.Create(1);

        await Producer(host).ProduceAsync(
            _topic,
            "key-1",
            message,
            "smoke-correlation",
            TestContext.Current.CancellationToken);

        var received = await recorder.NextAsync(Timeout);
        Assert.Equal(message, received.Message);
        Assert.Equal(message.EventId.ToString(), received.MessageId);
        Assert.Equal("smoke-correlation", received.CorrelationId);
        Assert.Equal("key-1", received.Key);
        Assert.Equal(_topic, received.Topic);
        Assert.Equal(0, received.Offset);
        Assert.Equal(1, received.Attempt);
    }

    [Fact]
    public async Task MessagesWithTheSameKey_AreConsumedInOrder()
    {
        var recorder = new StreamRecorder(_ => { });
        var host = await StartHostAsync(recorder);

        for (var sequence = 1; sequence <= 10; sequence++)
        {
            await Producer(host).ProduceAsync(
                _topic,
                "same-key",
                SmokeTestEvent.Create(sequence),
                cancellationToken: TestContext.Current.CancellationToken);
        }

        var received = new List<int>();
        for (var i = 0; i < 10; i++)
        {
            received.Add((await recorder.NextAsync(Timeout)).Message.Sequence);
        }

        Assert.Equal(Enumerable.Range(1, 10), received);
    }

    [Fact]
    public async Task CommittedOffsets_SurviveARestart_SoTheGroupDoesNotReprocess()
    {
        var first = new StreamRecorder(_ => { });
        var host = await StartHostAsync(first);
        await Producer(host).ProduceAsync(
            _topic,
            null,
            SmokeTestEvent.Create(1),
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, (await first.NextAsync(Timeout)).Message.Sequence);

        // Graceful shutdown commits the stored offset and leaves the group.
        await host.StopAsync(TestContext.Current.CancellationToken);

        var second = new StreamRecorder(_ => { });
        var restarted = await StartHostAsync(second);
        await Producer(restarted).ProduceAsync(
            _topic,
            null,
            SmokeTestEvent.Create(2),
            cancellationToken: TestContext.Current.CancellationToken);

        // The same group resumes after message 1: it only sees message 2.
        Assert.Equal(2, (await second.NextAsync(Timeout)).Message.Sequence);
        Assert.Equal(1, second.Attempts);
    }

    [Fact]
    public async Task FailingHandler_IsRetried_ThenSentToTheDeadLetterTopic_AndTheOffsetMovesOn()
    {
        var recorder = new StreamRecorder(context =>
        {
            if (context.Message.Sequence == 1)
            {
                throw new InvalidOperationException("Simulated handler failure");
            }
        });
        var host = await StartHostAsync(recorder, maxRetryAttempts: 2);

        await Producer(host).ProduceAsync(
            _topic,
            "k",
            SmokeTestEvent.Create(1),
            cancellationToken: TestContext.Current.CancellationToken);
        await Producer(host).ProduceAsync(
            _topic,
            "k",
            SmokeTestEvent.Create(2),
            cancellationToken: TestContext.Current.CancellationToken);

        // Message 1: first attempt + 2 retries; then message 2 is processed (the partition is not blocked).
        var attempts = new List<(int Sequence, int Attempt)>();
        for (var i = 0; i < 4; i++)
        {
            var context = await recorder.NextAsync(Timeout);
            attempts.Add((context.Message.Sequence, context.Attempt));
        }

        Assert.Equal([(1, 1), (1, 2), (1, 3), (2, 1)], attempts);

        var deadLetter = await _cluster!.ReadOneAsync(DeadLetterTopic, Timeout);
        Assert.Equal("k", deadLetter.Key);
        Assert.Equal(_topic, deadLetter.Headers["dlt-original-topic"]);
        Assert.Equal("0", deadLetter.Headers["dlt-original-offset"]);
        Assert.Equal(_groupId, deadLetter.Headers["dlt-consumer-group"]);
        Assert.Equal("Simulated handler failure", deadLetter.Headers["dlt-reason"]);
    }

    [Fact]
    public async Task HealthCheck_IsHealthy_WhileTheClusterIsReachable()
    {
        var host = await StartHostAsync(new StreamRecorder(_ => { }));

        var report = await host.Services.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Healthy, report.Entries["kafka"].Status);
    }

    [Fact]
    public async Task Produce_HonoursCancellation()
    {
        var host = await StartHostAsync(new StreamRecorder(_ => { }));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Producer(host).ProduceAsync(
                _topic,
                null,
                SmokeTestEvent.Create(1),
                cancellationToken: cancelled.Token));
    }

    private static IEventStreamProducer Producer(IHost host)
        => host.Services.GetRequiredService<IEventStreamProducer>();

    private async Task<IHost> StartHostAsync(StreamRecorder recorder, int maxRetryAttempts = 3)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Kafka:Consumer:GroupId"] = _groupId,
            ["Kafka:Consumer:MaxRetryAttempts"] = maxRetryAttempts.ToString(CultureInfo.InvariantCulture),
            ["Kafka:Consumer:RetryBackoffMs"] = "100",

            // The test owns its topic: provision it (1 partition keeps the ordering assertions simple) and its DLT.
            ["Kafka:ProvisionTopics"] = "true",
            ["Kafka:Topics:0:Name"] = _topic,
            ["Kafka:Topics:0:Partitions"] = "1",
        });

        builder.Services.AddSingleton(recorder);
        builder.Services.AddKafkaStreaming(builder.Configuration);
        builder.Services.AddKafkaConsumer<SmokeTestEvent, StreamRecordingHandler>(_topic);

        var host = builder.Build();
        _hosts.Add(host);
        await host.StartAsync(TestContext.Current.CancellationToken);
        await _cluster!.WaitForTopicAsync(_topic, Timeout);
        return host;
    }
}
