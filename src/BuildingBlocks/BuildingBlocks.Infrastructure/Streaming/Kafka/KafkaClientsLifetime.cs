using Confluent.Kafka.Admin;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Infrastructure.Streaming.Kafka;

/// <summary>
/// Startup and shutdown of the Kafka clients.
/// <list type="bullet">
///   <item>Startup (in the background, so the service starts even if Kafka is down): checks the cluster, logs a
///   clear message, and creates missing topics when <c>Kafka:ProvisionTopics</c> is on. Retries with backoff.</item>
///   <item>Shutdown: flushes the producer, so produced messages are not lost. Registered before every consumer, so
///   the host stops it LAST, after consumers have finished (and possibly dead-lettered) their messages.</item>
/// </list>
/// </summary>
internal sealed class KafkaClientsLifetime(
    KafkaAdmin admin,
    KafkaProducer producer,
    IOptions<KafkaOptions> options,
    IEnumerable<KafkaSubscription> subscriptions,
    ILogger<KafkaClientsLifetime> logger) : IHostedService, IDisposable
{
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);
    private readonly CancellationTokenSource _stopping = new();
    private Task _startup = Task.CompletedTask;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _startup = Task.Run(() => ConnectAndProvisionAsync(_stopping.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        await _startup.ConfigureAwait(false);

        // Give in-flight messages up to the producer's own timeout, bounded by the host shutdown timeout.
        var pending = await Task.Run(
                () => producer.Flush(TimeSpan.FromMilliseconds(options.Value.Producer.MessageTimeoutMs)),
                cancellationToken)
            .ConfigureAwait(false);
        KafkaLog.Flushed(logger, pending);
    }

    /// <inheritdoc />
    public void Dispose() => _stopping.Dispose();

    private async Task ConnectAndProvisionAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(1);
        var reported = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            var (brokers, error) = await admin.CheckAsync(stoppingToken).ConfigureAwait(false);
            if (error is null)
            {
                KafkaLog.Connected(logger, admin.Target, brokers);
                if (options.Value.ProvisionTopics)
                {
                    await admin.CreateMissingTopicsAsync(TopicsToProvision()).ConfigureAwait(false);
                }

                return;
            }

            if (!reported)
            {
                KafkaLog.Unavailable(logger, admin.Target, error);
                reported = true;
            }

            try
            {
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxRetryDelay.Ticks));
        }
    }

    private List<TopicSpecification> TopicsToProvision()
    {
        var settings = options.Value;
        var defaults = settings.TopicDefaults;

        var owned = settings.Topics.Select(topic =>
        {
            var specification = new TopicSpecification
            {
                Name = topic.Name,
                NumPartitions = topic.Partitions ?? defaults.Partitions,
                ReplicationFactor = topic.ReplicationFactor ?? defaults.ReplicationFactor,
            };
            if (topic.RetentionMs is { } retention)
            {
                specification.Configs = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["retention.ms"] = retention.ToString(System.Globalization.CultureInfo.InvariantCulture),
                };
            }

            return specification;
        });

        // Each consumer group owns its dead-letter topic.
        var deadLetter = subscriptions.Select(subscription => new TopicSpecification
        {
            Name = subscription.DeadLetterTopic(KafkaClientConfiguration.GroupId(settings)),
            NumPartitions = 1,
            ReplicationFactor = defaults.ReplicationFactor,
        });

        return [.. owned.Concat(deadLetter).DistinctBy(t => t.Name, StringComparer.Ordinal)];
    }
}

/// <summary>A consumer's topic and (optional) consumer group override.</summary>
/// <param name="Topic">The topic to consume.</param>
/// <param name="GroupId">The consumer group; <see langword="null"/> uses <c>Kafka:Consumer:GroupId</c>.</param>
internal sealed record KafkaSubscription(string Topic, string? GroupId)
{
    public const string DeadLetterSuffix = ".dlt";

    public string ResolveGroupId(string defaultGroupId) => GroupId ?? defaultGroupId;

    /// <summary>The consumer group's dead-letter topic: <c>&lt;topic&gt;.&lt;group&gt;.dlt</c>.</summary>
    public string DeadLetterTopic(string defaultGroupId)
        => $"{Topic}.{ResolveGroupId(defaultGroupId)}{DeadLetterSuffix}";
}
