using BuildingBlocks.Abstractions.Correlation;
using BuildingBlocks.Abstractions.Modules;

namespace BuildingBlocks.Infrastructure.Modules;

/// <summary>
/// The innermost handler of a module client, run for every attempt: sends the request to the process that hosts
/// the module (this one or another), with the internal API key and the current correlation ID.
/// </summary>
/// <param name="module">The called module.</param>
/// <param name="resolver">Resolves the module's address.</param>
/// <param name="key">The internal API key.</param>
/// <param name="correlation">The current correlation ID, when running in a web host.</param>
internal sealed class ModuleClientHandler(
    string module,
    ModuleAddressResolver resolver,
    InternalApiKey key,
    ICorrelationIdAccessor? correlation) : DelegatingHandler
{
    private readonly Uri _placeholder = ModuleClientRegistration.PlaceholderBaseAddress(module);

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.RequestUri is { } uri
            && string.Equals(uri.Host, _placeholder.Host, StringComparison.OrdinalIgnoreCase))
        {
            request.RequestUri = Rebase(uri, resolver.Resolve(module));
        }

        request.Headers.Remove(InternalApiOptions.HeaderName);
        request.Headers.TryAddWithoutValidation(InternalApiOptions.HeaderName, key.Value);

        if (correlation?.CorrelationId is { } correlationId && !request.Headers.Contains(CorrelationHeader.Name))
        {
            request.Headers.TryAddWithoutValidation(CorrelationHeader.Name, correlationId);
        }

        return base.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Moves <paramref name="uri"/> (built on the placeholder base address) onto <paramref name="target"/>, keeping
    /// any path prefix of the target (e.g. a gateway route) in front of <c>/internal/&lt;module&gt;/...</c>.
    /// </summary>
    /// <param name="uri">The request URI on the placeholder host.</param>
    /// <param name="target">The base address of the process hosting the module.</param>
    /// <returns>The URI to send the request to.</returns>
    internal static Uri Rebase(Uri uri, Uri target) =>
        new UriBuilder(target.Scheme, target.Host, target.Port)
        {
            Path = target.AbsolutePath.TrimEnd('/') + uri.AbsolutePath,
            Query = uri.Query.TrimStart('?'),
        }.Uri;
}
