using BuildingBlocks.Abstractions.Modules;
using Microsoft.Extensions.Hosting;

namespace BuildingBlocks.Infrastructure.Modules;

/// <summary>
/// Stops the host at startup, with instructions, when a module client cannot reach its module: the module is
/// neither enabled in this process nor given an address, or it is remote but no shared internal API key is set.
/// </summary>
/// <param name="resolver">The module address resolver.</param>
/// <param name="key">The internal API key of this process.</param>
internal sealed class ModuleClientsStartupCheck(ModuleAddressResolver resolver, InternalApiKey key) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var problems = resolver.Validate(key);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "Module clients are not configured correctly: " + string.Join("; ", problems)
                + ". See docs/modules.md.");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
