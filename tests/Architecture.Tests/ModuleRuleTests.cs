using System.Text.RegularExpressions;
using static Architecture.Tests.RepositoryProjects;

namespace Architecture.Tests;

/// <summary>
/// Enforces the modular monolith rules: one host composes every module, and modules stay independent of each
/// other, so any module can be extracted to its own process by configuration alone. See docs/modules.md.
/// </summary>
public sealed partial class ModuleRuleTests
{
    public static TheoryData<string> ModuleNames => new(Modules);

    public static TheoryData<string> ContractsProjectNames =>
        new(OfKind(ProjectKind.Contracts).Select(p => p.Name).Order(StringComparer.Ordinal));

    private static ProjectInfo Host => Assert.Single(OfKind(ProjectKind.Host));

    [Fact]
    public void Repository_HasExactlyOneHost() => Assert.Single(OfKind(ProjectKind.Host));

    /// <summary>
    /// The host is only a composition root: it reaches modules through their Api project (the <c>IModule</c>),
    /// never their Application, Infrastructure or Domain.
    /// </summary>
    [Fact]
    public void Host_ReferencesOnlyModuleApis_AndHostingBuildingBlocks()
    {
        var violations = Host.ProjectReferences
            .Where(r => !(All.TryGetValue(r, out var p) && p.Kind == ProjectKind.Api)
                && r is not (BuildingBlocksHosting or BuildingBlocksWeb))
            .ToList();

        Assert.True(
            violations.Count == 0,
            $"{Host.Name} may reference only <Module>.Api projects, {BuildingBlocksHosting} and {BuildingBlocksWeb}; "
            + $"found: {string.Join(", ", violations)}.");
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void EveryModule_HasAnApiProject_ReferencedByTheHost(string module) =>
        Assert.True(
            Host.ProjectReferences.Contains($"{module}.Api", StringComparer.Ordinal),
            $"{Host.Name} does not reference {module}.Api, so the {module} module can never run.");

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void EveryModule_DeclaresItsModuleClass_AndTheHostRegistersIt(string module)
    {
        var moduleClass = Path.Combine(All[$"{module}.Api"].Directory, $"{module}Module.cs");
        Assert.True(
            File.Exists(moduleClass),
            $"{module}.Api must declare {module}Module (IModule) in {module}Module.cs.");
        Assert.Contains($"class {module}Module : IModule", File.ReadAllText(moduleClass), StringComparison.Ordinal);

        var program = File.ReadAllText(Path.Combine(Host.Directory, "Program.cs"));
        Assert.True(
            program.Contains($"new {module}Module()", StringComparison.Ordinal),
            $"{Host.Name}/Program.cs must pass new {module}Module() to AddModules.");
    }

    /// <summary>Every module publishes a contracts project, the only project other modules may reference.</summary>
    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void EveryModule_HasAContractsProject(string module) =>
        Assert.True(
            All.TryGetValue(ContractsOf(module), out var contracts) && contracts.Kind == ProjectKind.Contracts,
            $"Missing src/Contracts/{ContractsOf(module)}: the {module} module's contract with other modules.");

    /// <summary>
    /// Contracts are what other modules (and, after a split, other processes) depend on: plain records with no
    /// dependencies, so they can become NuGet packages unchanged.
    /// </summary>
    [Theory]
    [MemberData(nameof(ContractsProjectNames))]
    public void Contracts_ReferenceOnlyBuildingBlocksAbstractions_AndNoPackages(string projectName)
    {
        var contracts = All[projectName];

        Assert.All(contracts.ProjectReferences, r => Assert.Equal(BuildingBlocksAbstractions, r));
        Assert.Empty(contracts.PackageReferences);
    }

    /// <summary>
    /// Project references already keep modules apart; this also catches fully qualified names and copied code
    /// that names another module's layers (e.g. <c>Catalog.Infrastructure.Persistence</c>) in a module or its
    /// contracts.
    /// </summary>
    [Fact]
    public void ModuleSources_DoNotNameAnotherModulesLayers()
    {
        var sources = ModuleProjects
            .Concat(OfKind(ProjectKind.Contracts))
            .Select(p => (Owner: p.Module ?? OwnerOfContracts(p.Name), Project: p))
            .SelectMany(x => SourceFiles(x.Project.Directory).Select(file => (x.Owner, File: file)));

        var violations = sources
            .SelectMany(s => LayerReference().Matches(File.ReadAllText(s.File))
                .Select(m => m.Groups["module"].Value)
                .Where(m => Modules.Contains(m, StringComparer.Ordinal))
                .Where(m => !string.Equals(m, s.Owner, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Select(m => $"{Path.GetRelativePath(Root, s.File)} -> {m}"))
            .ToList();

        Assert.True(
            violations.Count == 0,
            "Modules may use only another module's <Module>.Contracts, never its Domain, Application, "
            + "Infrastructure or Api:\n" + string.Join('\n', violations));
    }

    private static string? OwnerOfContracts(string projectName) =>
        IsModuleContracts(projectName) ? projectName[..^ContractsSuffix.Length] : null;

    private static IEnumerable<string> SourceFiles(string directory) =>
        Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path, "obj") && !IsBuildOutput(path, "bin"));

    private static bool IsBuildOutput(string path, string folder) =>
        path.Contains($"{Path.DirectorySeparatorChar}{folder}{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    [GeneratedRegex(
        @"\b(?<module>[A-Z][A-Za-z0-9]*)\.(?:Domain|Application|Infrastructure|Api)\b",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex LayerReference();
}
