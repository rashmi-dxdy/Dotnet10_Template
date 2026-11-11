namespace BuildingBlocks.Abstractions.Modules;

/// <summary>
/// Access control for the internal module APIs (<c>/internal/&lt;module&gt;/...</c>), from the <c>InternalApi</c>
/// section (environment variables <c>InternalApi__*</c>). Every internal request must carry the key in the
/// <see cref="HeaderName"/> header; module clients add it automatically (see <see cref="InternalApiKey"/>).
/// <list type="bullet">
///   <item>No <see cref="ApiKey"/> (the default, enough while every module runs in one process): a random key is
///   generated at startup, so only this process can call its internal APIs.</item>
///   <item><see cref="ApiKey"/> set (required once modules run in separate processes): every process uses the same
///   key, so they can call each other.</item>
/// </list>
/// The key is a secret: set it with user secrets or an environment variable, never in appsettings*.json.
/// </summary>
public sealed class InternalApiOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "InternalApi";

    /// <summary>The request header that carries the key.</summary>
    public const string HeaderName = "X-Internal-Api-Key";

    /// <summary>The minimum key length, so a guessable key is rejected at startup.</summary>
    public const int MinimumApiKeyLength = 32;

    /// <summary>Gets or sets the key shared by every process. Empty: a per-process key is generated.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Lists configuration problems, naming the setting and its environment variable, never the value.</summary>
    /// <returns>The problems; empty if the settings are valid.</returns>
    public IReadOnlyList<string> Validate() =>
        !string.IsNullOrEmpty(ApiKey) && ApiKey.Length < MinimumApiKeyLength
            ?
            [
                $"{SectionName}:ApiKey must be at least {MinimumApiKeyLength} characters "
                + $"(environment variable {SectionName}__ApiKey)",
            ]
            : [];
}
