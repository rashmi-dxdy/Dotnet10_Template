using BuildingBlocks.Abstractions.Modules;
using Microsoft.Extensions.Configuration;

namespace BuildingBlocks.Web.Modules;

/// <summary>
/// The modules the host knows, split by configuration into those this process hosts and those it does not.
/// </summary>
internal sealed class ModuleCatalog
{
    private ModuleCatalog(IReadOnlyList<IModule> hosted, IReadOnlyList<string> notHosted)
    {
        Hosted = hosted;
        NotHosted = notHosted;
        HostedNames = hosted.Select(m => m.Name).ToList();
    }

    /// <summary>Gets the modules enabled in this process, in registration order.</summary>
    public IReadOnlyList<IModule> Hosted { get; }

    /// <summary>Gets the names of the enabled modules.</summary>
    public IReadOnlyList<string> HostedNames { get; }

    /// <summary>Gets the names of the modules disabled in this process (hosted elsewhere, or not at all).</summary>
    public IReadOnlyList<string> NotHosted { get; }

    /// <summary>Validates the module list and applies <c>Modules:&lt;Name&gt;:Enabled</c>.</summary>
    /// <param name="modules">Every module the host can run.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The catalog.</returns>
    /// <exception cref="InvalidOperationException">A name is invalid or duplicated, or no module is enabled.</exception>
    public static ModuleCatalog Create(IReadOnlyList<IModule> modules, IConfiguration configuration)
    {
        if (modules.Count == 0 || modules.Any(m => m is null))
        {
            throw new InvalidOperationException("AddModules needs at least one module and no null entries.");
        }

        var invalid = modules.Where(m => !ModuleRoutes.IsValidName(m.Name)).Select(m => m.GetType().Name).ToList();
        if (invalid.Count > 0)
        {
            throw new InvalidOperationException(
                $"Invalid module name in {string.Join(", ", invalid)}: use lowercase letters, digits and hyphens, "
                + "starting with a letter (e.g. \"catalog\").");
        }

        var duplicates = modules.GroupBy(m => m.Name, StringComparer.Ordinal).Where(g => g.Count() > 1).ToList();
        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException(
                "Module names must be unique: " + string.Join(", ", duplicates.Select(g => g.Key)) + '.');
        }

        var hosted = new List<IModule>();
        var notHosted = new List<string>();
        foreach (var module in modules)
        {
            if (Settings(configuration, module.Name).Enabled)
            {
                hosted.Add(module);
            }
            else
            {
                notHosted.Add(module.Name);
            }
        }

        if (hosted.Count == 0)
        {
            throw new InvalidOperationException(
                "No module is enabled in this process. Set Modules:<Name>:Enabled (environment variable "
                + "Modules__<Name>__Enabled) to true for at least one of: "
                + string.Join(", ", modules.Select(m => m.Name)) + '.');
        }

        return new ModuleCatalog(hosted, notHosted);
    }

    private static ModuleSettings Settings(IConfiguration configuration, string module) =>
        configuration.GetSection(ModuleSettings.SectionFor(module)).Get<ModuleSettings>() ?? new ModuleSettings();
}
