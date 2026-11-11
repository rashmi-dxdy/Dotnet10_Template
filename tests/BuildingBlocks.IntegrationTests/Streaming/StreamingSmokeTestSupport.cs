using System.Text;
using System.Threading.Channels;
using BuildingBlocks.Abstractions.Messaging;
using BuildingBlocks.Abstractions.Streaming;
using Confluent.Kafka;

namespace BuildingBlocks.IntegrationTests.Streaming;

/// <summary>A generic, non-business event used only by the smoke tests.</summary>
/// <param name="EventId">The event ID.</param>
/// <param name="OccurredOn">When it was created.</param>
/// <param name="Sequence">The position in the test sequence.</param>
internal sealed record SmokeTestEvent(Guid EventId, DateTimeOffset OccurredOn, int Sequence) : IIntegrationEvent
{
    public static SmokeTestEvent Create(int sequence) => new(Guid.NewGuid(), DateTimeOffset.UtcNow, sequence);
}

/// <summary>Records every delivery; <paramref name="behavior"/> may throw to simulate a handler failure.</summary>
/// <param name="behavior">Runs for each delivery.</param>
internal sealed class StreamRecorder(Action<StreamMessageContext<SmokeTestEvent>> behavior)
{
    private readonly Channel<StreamMessageContext<SmokeTestEvent>> _received =
        Channel.CreateUnbounded<StreamMessageContext<SmokeTestEvent>>();

    private int _attempts;

    public int Attempts => Volatile.Read(ref _attempts);

    public void Record(StreamMessageContext<SmokeTestEvent> context)
    {
        Interlocked.Increment(ref _attempts);
        _received.Writer.TryWrite(context);
        behavior(context);
    }

    public async Task<StreamMessageContext<SmokeTestEvent>> NextAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        return await _received.Reader.ReadAsync(cts.Token);
    }
}

/// <summary>The smoke test handler: delegates to the recorder.</summary>
/// <param name="recorder">The shared recorder.</param>
internal sealed class StreamRecordingHandler(StreamRecorder recorder) : IStreamMessageHandler<SmokeTestEvent>
{
    public Task HandleAsync(StreamMessageContext<SmokeTestEvent> context, CancellationToken cancellationToken)
    {
        recorder.Record(context);
        return Task.CompletedTask;
    }
}

/// <summary>A message read back for assertions.</summary>
/// <param name="Key">The key.</param>
/// <param name="Headers">The headers, as UTF-8 strings.</param>
internal sealed record ReadMessage(string? Key, IReadOnlyDictionary<string, string> Headers);

/// <summary>
/// Direct cluster access for test setup and assertions, independent of the adapter under test. Skips the test
/// when Kafka is not configured or not reachable.
/// </summary>
internal sealed class KafkaTestCluster : IDisposable
{
    // Same setting as the services (Kafka__BrokerAddressFamily), e.g. V4 for Kafka in Docker on Windows.
    private static readonly BrokerAddressFamily AddressFamily =
        Enum.TryParse<BrokerAddressFamily>(
            Environment.GetEnvironmentVariable("Kafka__BrokerAddressFamily"),
            out var family)
            ? family
            : BrokerAddressFamily.Any;

    private readonly string _bootstrapServers;
    private readonly IAdminClient _admin;

    private KafkaTestCluster(string bootstrapServers)
    {
        _bootstrapServers = bootstrapServers;
        _admin = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = bootstrapServers,
            BrokerAddressFamily = AddressFamily,
        })
            .SetLogHandler((_, _) => { })
            .Build();
    }

    public static Task<KafkaTestCluster> ConnectOrSkipAsync(CancellationToken cancellationToken)
    {
        var bootstrapServers = Environment.GetEnvironmentVariable("Kafka__BootstrapServers");
        if (string.IsNullOrWhiteSpace(bootstrapServers))
        {
            Assert.Skip("Kafka is not configured: set Kafka__BootstrapServers (e.g. localhost:9092) to run the smoke "
                + "tests (see docs/streaming.md).");
        }

        var cluster = new KafkaTestCluster(bootstrapServers);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            cluster._admin.GetMetadata(TimeSpan.FromSeconds(10));
            return Task.FromResult(cluster);
        }
        catch (KafkaException exception)
        {
            cluster.Dispose();
            Assert.Skip($"Kafka at {bootstrapServers} is not reachable: {exception.Error.Reason}");
            throw;
        }
    }

    public async Task WaitForTopicAsync(string topic, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!_admin.GetMetadata(topic, TimeSpan.FromSeconds(5)).Topics
            .Exists(t => string.Equals(t.Topic, topic, StringComparison.Ordinal) && t.Error.Code == ErrorCode.NoError))
        {
            Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for topic {topic}.");
            await Task.Delay(200);
        }
    }

    public Task<ReadMessage> ReadOneAsync(string topic, TimeSpan timeout) =>
        Task.Run(() =>
        {
            using var consumer = new ConsumerBuilder<string?, byte[]>(new ConsumerConfig
            {
                BootstrapServers = _bootstrapServers,
                BrokerAddressFamily = AddressFamily,
                GroupId = $"smoke-test-reader-{Guid.NewGuid():N}",
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = false,
            })
                .SetLogHandler((_, _) => { })
                .Build();
            consumer.Subscribe(topic);
            using var cts = new CancellationTokenSource(timeout);
            var result = consumer.Consume(cts.Token);
            consumer.Close();

            return new ReadMessage(
                result.Message.Key,
                result.Message.Headers.ToDictionary(
                    h => h.Key,
                    h => Encoding.UTF8.GetString(h.GetValueBytes()),
                    StringComparer.Ordinal));
        });

    public async Task DeleteTopicsAsync(params string[] topics)
    {
        try
        {
            await _admin.DeleteTopicsAsync(topics);
        }
        catch (Confluent.Kafka.Admin.DeleteTopicsException)
        {
            // A topic that was never created (e.g. the DLT of a test without failures) is fine.
        }
    }

    public void Dispose() => _admin.Dispose();
}
