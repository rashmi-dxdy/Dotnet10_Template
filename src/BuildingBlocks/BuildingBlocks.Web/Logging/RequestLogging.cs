using Microsoft.AspNetCore.Http;
using Serilog;
using Serilog.AspNetCore;
using Serilog.Events;

namespace BuildingBlocks.Web.Logging;

/// <summary>
/// Settings for Serilog's request logging middleware: one structured log entry per HTTP request.
/// </summary>
internal static class RequestLogging
{
    private static readonly PathString HealthPath = new("/health");

    /// <summary>Applies the shared request logging settings.</summary>
    /// <param name="options">The options to configure.</param>
    public static void Configure(RequestLoggingOptions options)
    {
        options.MessageTemplate =
            "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";

        // Paths only: query strings, headers and bodies can carry tokens or personal data.
        options.IncludeQueryInRequestPath = false;
        options.GetLevel = GetLevel;
        options.EnrichDiagnosticContext = Enrich;
    }

    private static LogEventLevel GetLevel(HttpContext context, double elapsedMilliseconds, Exception? exception)
    {
        if (exception is not null || context.Response.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            return LogEventLevel.Error;
        }

        // Health probes run every few seconds; keep them out of normal logs.
        return context.Request.Path.StartsWithSegments(HealthPath, StringComparison.OrdinalIgnoreCase)
            ? LogEventLevel.Verbose
            : LogEventLevel.Information;
    }

    private static void Enrich(IDiagnosticContext diagnosticContext, HttpContext context)
    {
        diagnosticContext.Set("RequestScheme", context.Request.Scheme);
        diagnosticContext.Set("RequestHost", context.Request.Host.Value);

        if (context.GetEndpoint()?.DisplayName is { } endpoint)
        {
            diagnosticContext.Set("EndpointName", endpoint);
        }
    }
}
