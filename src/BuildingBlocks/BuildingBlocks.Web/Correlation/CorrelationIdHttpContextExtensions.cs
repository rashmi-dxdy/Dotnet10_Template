using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Web.Correlation;

/// <summary>
/// Access to the correlation ID assigned by <see cref="CorrelationIdMiddleware"/>.
/// </summary>
public static class CorrelationIdHttpContextExtensions
{
    /// <summary>Gets the correlation ID of the current request, if the middleware has run.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>The correlation ID, or <see langword="null"/>.</returns>
    public static string? GetCorrelationId(this HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var value) ? value as string : null;
    }
}
