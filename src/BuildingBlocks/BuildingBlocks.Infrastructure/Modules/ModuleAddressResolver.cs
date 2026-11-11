using BuildingBlocks.Abstractions.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Infrastructure.Modules;

/// <summary>
/// Decides where a module is called: at <c>Modules:&lt;Name&gt;:BaseAddress</c> when it is set, otherwise at this
/// process's own HTTP address when the module is enabled here.
/// </summary>
/// <param name="registrations">The registered module clients.</param>
/// <param name="serviceProvider">Resolves the optional <see cref="IModuleHost"/> (absent outside a web host).</param>
internal sealed class ModuleAddressResolver(
    IEnumerable<ModuleClientRegistration> registrations,
    IServiceProvider serviceProvider)
{
    private readonly Dictionary<string, Uri?> _baseAddresses = registrations
        .GroupBy(r => r.Module, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.First().BaseAddress, StringComparer.Ordinal);

    /// <summary>Gets the base address to call <paramref name="module"/> at.</summary>
    /// <param name="module">The module name.</param>
    /// <returns>The base address of the process hosting the module.</returns>
    /// <exception cref="ModuleCallException">The module is neither hosted here nor configured elsewhere.</exception>
    public Uri Resolve(string module)
    {
        if (_baseAddresses.TryGetValue(module, out var remote) && remote is not null)
        {
            return remote;
        }

        var host = serviceProvider.GetService<IModuleHost>();
        if (host?.IsHosted(module) == true)
        {
            try
            {
                return host.GetLocalBaseAddress();
            }
            catch (InvalidOperationException exception)
            {
                throw new ModuleCallException(module, statusCode: null, exception.Message, exception);
            }
        }

        throw new ModuleCallException(module, statusCode: null, NotReachableMessage(module));
    }

    /// <summary>
    /// Checks that every registered client can reach its module, so a wrong configuration stops the host at
    /// startup instead of failing the first call.
    /// </summary>
    /// <param name="key">The internal API key of this process.</param>
    /// <returns>The problems; empty if every client is configured.</returns>
    public IReadOnlyList<string> Validate(InternalApiKey key)
    {
        var host = serviceProvider.GetService<IModuleHost>();
        var problems = new List<string>();

        foreach (var (module, baseAddress) in _baseAddresses)
        {
            if (baseAddress is null && host?.IsHosted(module) != true)
            {
                problems.Add(NotReachableMessage(module));
            }
            else if (baseAddress is not null && !key.IsShared)
            {
                problems.Add(
                    $"module '{module}' is called at {baseAddress} (another process), but {InternalApiOptions.SectionName}"
                    + $":ApiKey is not set, so it would reject every call. Set the same key in every process "
                    + $"(environment variable {InternalApiOptions.SectionName}__ApiKey)");
            }
        }

        return problems;
    }

    private static string NotReachableMessage(string module) =>
        $"module '{module}' is not hosted in this process and {ModuleSettings.SectionFor(module)}:BaseAddress is not "
        + $"set. Either enable it here ({ModuleSettings.SectionFor(module)}:Enabled = true) or set its address "
        + $"(environment variable {ModuleSettings.SectionName}__{module}__BaseAddress)";
}
