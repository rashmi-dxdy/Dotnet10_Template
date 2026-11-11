using BuildingBlocks.Abstractions.Modules;

namespace BuildingBlocks.Infrastructure.Modules;

/// <summary>A module client registered by <c>AddModuleClient</c>: which module it calls, and where.</summary>
/// <param name="Module">The called module, e.g. <c>catalog</c>.</param>
/// <param name="BaseAddress">
/// <c>Modules:&lt;Name&gt;:BaseAddress</c> when the module runs in another process; <see langword="null"/> when it
/// runs in this one.
/// </param>
/// <param name="ClientType">The client interface, for error messages.</param>
internal sealed record ModuleClientRegistration(string Module, Uri? BaseAddress, Type ClientType)
{
    /// <summary>
    /// Gets the placeholder base address every module client is created with, e.g.
    /// <c>http://catalog.module/internal/catalog/</c>. <see cref="ModuleClientHandler"/> replaces scheme, host and
    /// port per request, once the real address is known (the local server's address only exists after startup).
    /// </summary>
    /// <param name="module">The module name.</param>
    /// <returns>The placeholder base address, with a trailing slash so relative paths append to it.</returns>
    public static Uri PlaceholderBaseAddress(string module) =>
        new($"{Uri.UriSchemeHttp}://{module}.module{ModuleRoutes.Internal(module)}/");
}
