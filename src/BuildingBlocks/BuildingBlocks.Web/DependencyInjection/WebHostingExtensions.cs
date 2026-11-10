using BuildingBlocks.Hosting.DependencyInjection;
using BuildingBlocks.Web.Correlation;
using BuildingBlocks.Web.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace BuildingBlocks.Web.DependencyInjection;

/// <summary>
/// Host setup shared by every HTTP API.
/// </summary>
public static class WebHostingExtensions
{
    /// <summary>
    /// Applies the shared host defaults (<see cref="HostingBuilderExtensions.AddServiceDefaults"/>) and
    /// RFC 9457 problem details that carry the correlation ID.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static WebApplicationBuilder AddWebServiceDefaults(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddServiceDefaults();

        builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            if (context.HttpContext.GetCorrelationId() is { } correlationId)
            {
                context.ProblemDetails.Extensions["correlationId"] = correlationId;
            }
        });

        return builder;
    }

    /// <summary>
    /// Adds the shared middleware, outermost first:
    /// <list type="number">
    ///   <item>correlation ID, so every later log entry carries it;</item>
    ///   <item>Serilog request logging, one entry per request with status and duration;</item>
    ///   <item>exception handler, which logs unhandled exceptions and returns a problem details response
    ///   without exception details.</item>
    /// </list>
    /// Call it right after <c>Build()</c>, before any other middleware or endpoint.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <returns>The same application, for chaining.</returns>
    public static WebApplication UseWebServiceDefaults(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseSerilogRequestLogging(RequestLogging.Configure);
        app.UseExceptionHandler();

        return app;
    }
}
