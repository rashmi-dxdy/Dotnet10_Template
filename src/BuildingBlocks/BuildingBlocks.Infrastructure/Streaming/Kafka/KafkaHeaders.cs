using System.Text;
using Confluent.Kafka;

namespace BuildingBlocks.Infrastructure.Streaming.Kafka;

/// <summary>Message header names and helpers. Header values are UTF-8 strings.</summary>
internal static class KafkaHeaders
{
    public const string MessageId = "message-id";
    public const string CorrelationId = "correlation-id";
    public const string MessageType = "message-type";
    public const string ContentType = "content-type";

    // Added to dead-lettered messages, so they can be inspected and replayed.
    public const string DeadLetterOriginalTopic = "dlt-original-topic";
    public const string DeadLetterOriginalPartition = "dlt-original-partition";
    public const string DeadLetterOriginalOffset = "dlt-original-offset";
    public const string DeadLetterConsumerGroup = "dlt-consumer-group";
    public const string DeadLetterReason = "dlt-reason";
    public const string DeadLetterExceptionType = "dlt-exception-type";

    public static void Set(Headers headers, string name, string? value)
    {
        if (value is not null)
        {
            headers.Remove(name);
            headers.Add(name, Encoding.UTF8.GetBytes(value));
        }
    }

    public static string? Get(Headers? headers, string name) =>
        headers is not null && headers.TryGetLastBytes(name, out var bytes) ? Encoding.UTF8.GetString(bytes) : null;
}
