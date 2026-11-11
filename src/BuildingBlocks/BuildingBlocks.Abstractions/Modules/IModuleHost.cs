namespace BuildingBlocks.Abstractions.Modules;

/// <summary>
/// What the current process hosts. Implemented by the web host. Module clients use it to send calls for a module
/// that runs in this process to this process's own HTTP endpoint.
/// </summary>
public interface IModuleHost
{
    /// <summary>Gets the names of the modules this process hosts.</summary>
    IReadOnlyCollection<string> HostedModules { get; }

    /// <summary>Whether this process hosts <paramref name="moduleName"/>.</summary>
    /// <param name="moduleName">The module name, e.g. <c>catalog</c>.</param>
    /// <returns><see langword="true"/> if the module is enabled in this process.</returns>
    bool IsHosted(string moduleName);

    /// <summary>
    /// Gets the loopback address of this process's HTTP server, e.g. <c>http://127.0.0.1:8080/</c>. Available once
    /// the server has started.
    /// </summary>
    /// <returns>The base address.</returns>
    /// <exception cref="InvalidOperationException">The server is not listening on any HTTP address (yet).</exception>
    Uri GetLocalBaseAddress();
}
