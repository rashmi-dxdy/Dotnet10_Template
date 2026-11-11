using BuildingBlocks.Abstractions.Correlation;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Web.Correlation;

/// <summary>
/// <see cref="ICorrelationIdAccessor"/> for HTTP APIs: the correlation ID that <see cref="CorrelationIdMiddleware"/>
/// assigned to the current request.
/// </summary>
/// <param name="httpContextAccessor">Access to the current request.</param>
internal sealed class HttpContextCorrelationIdAccessor(IHttpContextAccessor httpContextAccessor)
    : ICorrelationIdAccessor
{
    /// <inheritdoc />
    public string? CorrelationId => httpContextAccessor.HttpContext?.GetCorrelationId();
}
