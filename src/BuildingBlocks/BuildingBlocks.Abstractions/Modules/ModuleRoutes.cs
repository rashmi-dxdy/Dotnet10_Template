namespace BuildingBlocks.Abstractions.Modules;

/// <summary>
/// The URL conventions of every module, shared by the host that maps a module's endpoints and by the clients
/// that call them, so both sides always agree. A module named <c>catalog</c> serves:
/// <list type="bullet">
///   <item><c>/api/catalog/...</c>: its public API, for external clients (through the gateway / ingress);</item>
///   <item><c>/internal/catalog/...</c>: its internal API, for other modules only (never exposed publicly).</item>
/// </list>
/// The paths stay the same when a module moves to its own process, so its callers don't change.
/// </summary>
public static class ModuleRoutes
{
    /// <summary>The prefix of every public module API.</summary>
    public const string PublicPrefix = "/api";

    /// <summary>The prefix of every internal module API. Block it at the gateway / ingress.</summary>
    public const string InternalPrefix = "/internal";

    private const int MaxNameLength = 50;

    /// <summary>Gets the public API base path of <paramref name="module"/>, e.g. <c>/api/catalog</c>.</summary>
    /// <param name="module">The module name, e.g. <c>catalog</c>.</param>
    /// <returns>The base path, without a trailing slash.</returns>
    public static string Public(string module) => $"{PublicPrefix}/{module}";

    /// <summary>Gets the internal API base path of <paramref name="module"/>, e.g. <c>/internal/catalog</c>.</summary>
    /// <param name="module">The module name, e.g. <c>catalog</c>.</param>
    /// <returns>The base path, without a trailing slash.</returns>
    public static string Internal(string module) => $"{InternalPrefix}/{module}";

    /// <summary>
    /// Whether <paramref name="module"/> is a valid module name: lowercase letters, digits and hyphens, starting
    /// with a letter. The name is used in URLs, configuration keys, log properties and consumer group names.
    /// </summary>
    /// <param name="module">The candidate name.</param>
    /// <returns><see langword="true"/> if the name is valid.</returns>
    public static bool IsValidName(string? module) =>
        !string.IsNullOrEmpty(module)
        && module.Length <= MaxNameLength
        && char.IsAsciiLetterLower(module[0])
        && module.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');
}
