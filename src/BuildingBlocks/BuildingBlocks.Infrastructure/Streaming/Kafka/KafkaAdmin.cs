using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Infrastructure.Streaming.Kafka;

/// <summary>
/// Cluster checks and topic creation, on one long-lived admin client.
/// </summary>
internal sealed class KafkaAdmin : IDisposable
{
    private readonly KafkaOptions _options;
    private readonly ILogger<KafkaAdmin> _logger;
    private readonly IAdminClient _admin;

    public KafkaAdmin(IOptions<KafkaOptions> options, ILogger<KafkaAdmin> logger)
    {
        _options = options.Value;
        _logger = logger;
        var events = new KafkaClientEvents(logger, "admin");
        _admin = new AdminClientBuilder(KafkaClientConfiguration.Admin(_options))
            .SetErrorHandler((_, error) => events.OnError(error))
            .SetLogHandler((_, message) => events.OnLog(message))
            .Build();
    }

    /// <summary>Gets the cluster description that is safe to log.</summary>
    public string Target => KafkaClientConfiguration.Target(_options);

    /// <summary>Asks the cluster for its brokers, within <c>Kafka:AdminTimeoutSeconds</c>.</summary>
    /// <param name="cancellationToken">A token to stop waiting.</param>
    /// <returns>The number of brokers, or the reason the cluster is unreachable.</returns>
    public async Task<(int Brokers, string? Error)> CheckAsync(CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(_options.AdminTimeoutSeconds);
        try
        {
            // GetMetadata is synchronous in Confluent.Kafka; keep it off the caller's thread.
            var metadata = await Task.Run(() => _admin.GetMetadata(timeout), cancellationToken).ConfigureAwait(false);
            return metadata.Brokers.Count > 0 ? (metadata.Brokers.Count, null) : (0, "the cluster reported no brokers");
        }
        catch (KafkaException exception)
        {
            return (0, $"{exception.Error.Reason} ({exception.Error.Code}). Is Kafka running "
                + "(e.g. `docker compose up -d kafka`) and is Kafka:BootstrapServers correct? With Docker, the "
                + "broker's advertised listener must be reachable from this service");
        }
    }

    /// <summary>Creates the topics that do not exist yet. Existing topics are never changed.</summary>
    /// <param name="topics">The topics.</param>
    /// <returns>A task that completes when all creations were attempted.</returns>
    public async Task CreateMissingTopicsAsync(IReadOnlyCollection<TopicSpecification> topics)
    {
        if (topics.Count == 0)
        {
            return;
        }

        try
        {
            await _admin.CreateTopicsAsync(
                    topics,
                    new CreateTopicsOptions { RequestTimeout = TimeSpan.FromSeconds(_options.AdminTimeoutSeconds * 2) })
                .ConfigureAwait(false);
            foreach (var topic in topics)
            {
                KafkaLog.TopicCreated(_logger, topic.Name, topic.NumPartitions, topic.ReplicationFactor);
            }
        }
        catch (CreateTopicsException exception)
        {
            foreach (var result in exception.Results)
            {
                var topic = topics.First(t => string.Equals(t.Name, result.Topic, StringComparison.Ordinal));
                if (result.Error.Code == ErrorCode.NoError)
                {
                    KafkaLog.TopicCreated(_logger, topic.Name, topic.NumPartitions, topic.ReplicationFactor);
                }
                else if (result.Error.Code != ErrorCode.TopicAlreadyExists)
                {
                    KafkaLog.TopicCreationFailed(_logger, result.Topic, result.Error.Reason);
                }
            }
        }
    }

    /// <inheritdoc />
    public void Dispose() => _admin.Dispose();
}
