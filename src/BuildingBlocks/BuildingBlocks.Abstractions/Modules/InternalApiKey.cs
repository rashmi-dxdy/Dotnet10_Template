using System.Security.Cryptography;
using System.Text;

namespace BuildingBlocks.Abstractions.Modules;

/// <summary>
/// The key that authorizes calls to the internal module APIs, shared by the host (which checks it) and the module
/// clients (which send it). It is <c>InternalApi:ApiKey</c> when configured. Otherwise a random key is generated
/// per process, so the application can call its own internal APIs but no other process can.
/// </summary>
public sealed class InternalApiKey
{
    private const int GeneratedKeyBytes = 32;

    private readonly byte[] _bytes;

    /// <summary>Initializes a new instance of the <see cref="InternalApiKey"/> class.</summary>
    /// <param name="configuredKey">The configured key (<c>InternalApi:ApiKey</c>), or empty to generate one.</param>
    public InternalApiKey(string? configuredKey)
    {
        IsShared = !string.IsNullOrEmpty(configuredKey);
        Value = IsShared
            ? configuredKey!
            : Convert.ToHexString(RandomNumberGenerator.GetBytes(GeneratedKeyBytes));
        _bytes = Encoding.UTF8.GetBytes(Value);
    }

    /// <summary>Gets the key to send in the <see cref="InternalApiOptions.HeaderName"/> header.</summary>
    public string Value { get; }

    /// <summary>
    /// Gets a value indicating whether the key is configured, so other processes configured with the same key can
    /// call this one. <see langword="false"/>: generated, valid in this process only.
    /// </summary>
    public bool IsShared { get; }

    /// <summary>Compares <paramref name="candidate"/> with the key in constant time.</summary>
    /// <param name="candidate">The key sent by the caller.</param>
    /// <returns><see langword="true"/> if it matches.</returns>
    public bool Matches(string? candidate) =>
        !string.IsNullOrEmpty(candidate)
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(candidate), _bytes);

    /// <summary>Describes the key without revealing it.</summary>
    /// <returns>A description safe to log.</returns>
    public override string ToString() =>
        IsShared ? "shared key (InternalApi:ApiKey)" : "per-process key (InternalApi:ApiKey not set)";
}
