using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Infrastructure.Streaming.Kafka;

/// <summary>
/// Routes librdkafka errors to <see cref="ILogger"/> (instead of stderr) without flooding the log while a broker is
/// down. The client reports a failed connection every few seconds with a slightly different text each time
/// (timings), and reports it twice (as a log line and as an error event). So: only error events are logged, at most
/// once a minute per error code, with the number of repeats that were suppressed.
/// </summary>
/// <param name="logger">The logger.</param>
/// <param name="clientName">Which client (producer, admin, consumer of topic X) the events come from.</param>
internal sealed class KafkaClientEvents(ILogger logger, string clientName)
{
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromMinutes(1);
    private readonly Lock _gate = new();
    private readonly Dictionary<ErrorCode, (DateTime LastLoggedUtc, int Suppressed)> _seen = [];

    public void OnError(Error error)
    {
        if (error.IsFatal)
        {
            KafkaLog.ClientFatalError(logger, clientName, error.Reason, error.Code);
            return;
        }

        if (TryTakeLogSlot(error.Code, out var suppressed))
        {
            var reason = suppressed > 0 ? $"{error.Reason} (repeated {suppressed} more time(s))" : error.Reason;
            KafkaLog.ClientError(logger, clientName, reason, error.Code);
        }
    }

    public void OnLog(LogMessage message)
    {
        // Warnings and errors also arrive through OnError; only log what has no error event.
        if (message.Level <= SyslogLevel.Critical)
        {
            KafkaLog.ClientError(logger, clientName, message.Message, ErrorCode.Unknown);
        }
    }

    private bool TryTakeLogSlot(ErrorCode code, out int suppressed)
    {
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            if (_seen.TryGetValue(code, out var entry) && now - entry.LastLoggedUtc < RepeatInterval)
            {
                _seen[code] = (entry.LastLoggedUtc, entry.Suppressed + 1);
                suppressed = 0;
                return false;
            }

            suppressed = entry.Suppressed;
            _seen[code] = (now, 0);
            return true;
        }
    }
}
