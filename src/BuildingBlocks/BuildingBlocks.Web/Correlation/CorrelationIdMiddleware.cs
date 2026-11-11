using System.Diagnostics;
using BuildingBlocks.Abstractions.Correlation;
using BuildingBlocks.Hosting.Logging;
using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace BuildingBlocks.Web.Correlation;

/// <summary>
/// Gives every HTTP request a correlation ID. An incoming <c>X-Correlation-ID</c> header is reused if it is
/// well formed, otherwise a new ID is generated. The ID is returned in the response header, added to every
/// log entry written during the request (<see cref="LogProperties.CorrelationId"/>) and tagged on the
/// current trace activity.
/// </summary>
/// <param name="next">The next middleware.</param>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    /// <summary>The HTTP header that carries the correlation ID.</summary>
    public const string HeaderName = CorrelationHeader.Name;

    internal const string ItemKey = "CorrelationId";

    private const int MaxLength = 64;

    /// <summary>Processes the request.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A task that completes when the rest of the pipeline has run.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var correlationId = IsValid(context.Request.Headers[HeaderName].ToString())
            ? context.Request.Headers[HeaderName].ToString()
            : Guid.NewGuid().ToString("N");

        context.Items[ItemKey] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;
        Activity.Current?.SetTag("correlation.id", correlationId);

        using (LogContext.PushProperty(LogProperties.CorrelationId, correlationId))
        {
            await next(context).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Accepts only short IDs made of letters, digits and <c>- _ . :</c>, so a client cannot inject log
    /// lines, markup or huge values through the header.
    /// </summary>
    /// <param name="value">The header value.</param>
    /// <returns>Whether the value can be used as a correlation ID.</returns>
    internal static bool IsValid(string? value) =>
        !string.IsNullOrEmpty(value)
        && value.Length <= MaxLength
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':');
}
