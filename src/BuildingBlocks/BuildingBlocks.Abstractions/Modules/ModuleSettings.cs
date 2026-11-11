namespace BuildingBlocks.Abstractions.Modules;

/// <summary>
/// Where one module runs, from <c>Modules:&lt;Name&gt;</c> (environment variables <c>Modules__&lt;Name&gt;__*</c>).
/// These two settings move a module out of the monolith without code changes:
/// <list type="bullet">
///   <item><see cref="Enabled"/>: whether THIS process hosts the module (its endpoints, consumers and DbContext).</item>
///   <item><see cref="BaseAddress"/>: where other modules call it. Empty means "in this process".</item>
/// </list>
/// </summary>
public sealed class ModuleSettings
{
    /// <summary>The configuration section that holds one sub-section per module.</summary>
    public const string SectionName = "Modules";

    /// <summary>Gets or sets a value indicating whether this process hosts the module. Default: <see langword="true"/>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the base address of the process that hosts the module, e.g. <c>http://catalog:8080</c>, when
    /// it is not this process. Empty: calls go to this process (the module must then be enabled here).
    /// </summary>
    public string? BaseAddress { get; set; }

    /// <summary>Gets the configuration path of <paramref name="module"/>'s settings, e.g. <c>Modules:catalog</c>.</summary>
    /// <param name="module">The module name.</param>
    /// <returns>The configuration path.</returns>
    public static string SectionFor(string module) => $"{SectionName}:{module}";
}
