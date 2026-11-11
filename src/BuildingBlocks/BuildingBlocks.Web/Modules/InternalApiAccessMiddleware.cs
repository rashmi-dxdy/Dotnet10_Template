using BuildingBlocks.Abstractions.Modules;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Web.Modules;

/// <summary>
/// Guards every <c>/internal/...</c> request (including unknown paths, so modules cannot be probed): it must carry
/// the internal API key in <see cref="InternalApiOptions.HeaderName"/>, otherwise it gets <c>403</c>.
/// </summary>
/// <param name="next">The next middleware.</param>
/// <param name="key">The key of this process.</param>
/// <param name="logger">The logger.</param>
internal sealed class InternalApiAccessMiddleware(
    RequestDelegate next,
    InternalApiKey key,
    ILogger<InternalApiAccessMiddleware> logger)
{
    /// <summary>Processes the request.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A task that completes when the request has been handled.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments(ModuleRoutes.InternalPrefix, StringComparison.OrdinalIgnoreCase)
            || key.Matches(context.Request.Headers[InternalApiOptions.HeaderName].ToString()))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        ModuleLog.InternalApiRejected(logger, context.Request.Path, context.Connection.RemoteIpAddress?.ToString());

        // No problem details: nothing about the internal API is revealed to a caller without the key.
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
    }
}
