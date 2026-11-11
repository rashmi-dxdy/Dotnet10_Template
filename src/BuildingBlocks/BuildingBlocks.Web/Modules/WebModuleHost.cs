using BuildingBlocks.Abstractions.Modules;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace BuildingBlocks.Web.Modules;

/// <summary>
/// <see cref="IModuleHost"/> for the ASP.NET Core host: the enabled modules, and the loopback address of the
/// running server (calls between modules in the same process go through it, exactly like remote calls).
/// </summary>
/// <param name="catalog">The modules of this process.</param>
/// <param name="server">The HTTP server, whose listening addresses are known once it has started.</param>
internal sealed class WebModuleHost(ModuleCatalog catalog, IServer server) : IModuleHost
{
    /// <inheritdoc />
    public IReadOnlyCollection<string> HostedModules => catalog.HostedNames;

    /// <inheritdoc />
    public bool IsHosted(string moduleName) => catalog.HostedNames.Contains(moduleName, StringComparer.Ordinal);

    /// <inheritdoc />
    public Uri GetLocalBaseAddress()
    {
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [];

        // Prefer plain HTTP: loopback traffic never leaves the machine and needs no certificate.
        return addresses
                .Select(ToLoopback)
                .OfType<Uri>()
                .OrderBy(uri => string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) ? 0 : 1)
                .FirstOrDefault()
            ?? throw new InvalidOperationException(
                "The HTTP server is not listening on any address yet, so modules in this process cannot be called. "
                + "Set Modules:<Name>:BaseAddress to call a module at an explicit address.");
    }

    /// <summary>
    /// Turns a listening address into one this process can connect to: wildcard hosts (<c>+</c>, <c>*</c>,
    /// <c>0.0.0.0</c>, <c>[::]</c>) become loopback. Addresses with port 0 (not bound yet) are skipped.
    /// </summary>
    /// <param name="address">An address reported by the server, e.g. <c>http://[::]:8080</c>.</param>
    /// <returns>The base address, or <see langword="null"/> if it cannot be used.</returns>
    internal static Uri? ToLoopback(string address)
    {
        var normalized = address
            .Replace("://+:", "://localhost:", StringComparison.Ordinal)
            .Replace("://*:", "://localhost:", StringComparison.Ordinal);

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) || uri.Port == 0)
        {
            return null;
        }

        var host = uri.Host switch
        {
            "0.0.0.0" => "127.0.0.1",
            "[::]" => "[::1]",
            _ => uri.Host,
        };

        return new UriBuilder(uri.Scheme, host, uri.Port).Uri;
    }
}
