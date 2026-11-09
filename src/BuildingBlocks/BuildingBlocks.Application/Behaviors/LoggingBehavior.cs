using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Application.Behaviors;

/// <summary>
/// Logs the start, end and duration of every request that goes through MediatR.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <param name="logger">The logger.</param>
public sealed partial class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <inheritdoc />
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (!logger.IsEnabled(LogLevel.Information))
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }

        var requestName = typeof(TRequest).Name;
        LogHandling(logger, requestName);

        var start = Stopwatch.GetTimestamp();
        var response = await next(cancellationToken).ConfigureAwait(false);

        var elapsedMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        LogHandled(logger, requestName, elapsedMilliseconds);
        return response;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Handling {RequestName}")]
    private static partial void LogHandling(ILogger logger, string requestName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled {RequestName} in {ElapsedMilliseconds:0.0} ms")]
    private static partial void LogHandled(ILogger logger, string requestName, double elapsedMilliseconds);
}
