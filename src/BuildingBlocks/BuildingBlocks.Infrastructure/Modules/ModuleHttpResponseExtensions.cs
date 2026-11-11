using System.Text.Json;
using BuildingBlocks.Abstractions.Modules;

namespace BuildingBlocks.Infrastructure.Modules;

/// <summary>Error handling for responses from another module's internal API.</summary>
public static class ModuleHttpResponseExtensions
{
    private const int MaxDetailLength = 500;

    private static readonly string[] ProblemTextProperties = ["title", "detail"];

    /// <summary>
    /// Throws a <see cref="ModuleCallException"/> (with the status code, and the problem details title / detail when
    /// the module sent them) if the response is not a success. Check the status codes your caller handles first,
    /// e.g. <c>404</c> for "not found".
    /// </summary>
    /// <param name="response">The response.</param>
    /// <param name="module">The called module, e.g. <c>catalog</c>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the response is known to be a success.</returns>
    /// <exception cref="ModuleCallException">The response is not a success.</exception>
    public static async Task EnsureModuleSuccessAsync(
        this HttpResponseMessage response,
        string module,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var status = (int)response.StatusCode;
        var problem = await ReadProblemAsync(response, cancellationToken).ConfigureAwait(false);
        var message = $"Module '{module}' answered {status} {response.ReasonPhrase} to "
            + $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.AbsolutePath}"
            + (problem is null ? "." : $": {problem}");

        throw new ModuleCallException(module, status, message);
    }

    private static async Task<string?> ReadProblemAsync(HttpResponseMessage response, CancellationToken token)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is null || !mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var body = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            await using (body.ConfigureAwait(false))
            {
                using var document = await JsonDocument.ParseAsync(body, cancellationToken: token)
                    .ConfigureAwait(false);
                var parts = ProblemTextProperties
                    .Select(name => document.RootElement.ValueKind == JsonValueKind.Object
                        && document.RootElement.TryGetProperty(name, out var value)
                        && value.ValueKind == JsonValueKind.String
                            ? value.GetString()
                            : null)
                    .OfType<string>()
                    .ToList();

                var text = string.Join(" ", parts);
                return text.Length == 0 ? null : text[..Math.Min(text.Length, MaxDetailLength)];
            }
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
