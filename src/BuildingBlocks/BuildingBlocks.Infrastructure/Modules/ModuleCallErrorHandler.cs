using BuildingBlocks.Abstractions.Modules;
using Polly;

namespace BuildingBlocks.Infrastructure.Modules;

/// <summary>
/// The outermost handler of a module client: once the resilience pipeline has given up (connection failures,
/// timeouts, open circuit), turns the transport exception into a <see cref="ModuleCallException"/> naming the
/// module, so Application code can handle every failed module call the same way.
/// </summary>
/// <param name="module">The called module.</param>
internal sealed class ModuleCallErrorHandler(string module) : DelegatingHandler
{
    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or ExecutionRejectedException)
        {
            throw new ModuleCallException(
                module,
                statusCode: null,
                $"Module '{module}' could not be reached: {exception.Message}",
                exception);
        }
    }
}
