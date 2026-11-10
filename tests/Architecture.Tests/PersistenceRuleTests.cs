using System.Text.RegularExpressions;
using static Architecture.Tests.RepositoryProjects;

namespace Architecture.Tests;

/// <summary>
/// Enforces the database ownership model: EF Core lives in Infrastructure only, and every DbContext belongs
/// to exactly one service. See docs/database.md.
/// </summary>
public sealed partial class PersistenceRuleTests
{
    private const string DesignTimePackagePrefix = "Microsoft.EntityFrameworkCore.";

    private static readonly string[] DesignTimePackages =
        ["Microsoft.EntityFrameworkCore.Design", "Microsoft.EntityFrameworkCore.Tools"];

    public static TheoryData<string> ProjectNames => new(All.Keys.Order(StringComparer.Ordinal));

    /// <summary>
    /// EF Core and Npgsql runtime packages: service Infrastructure projects and BuildingBlocks.Infrastructure
    /// only. Domain, Application, Contracts and hosts reach the database through Infrastructure.
    /// </summary>
    [Theory]
    [MemberData(nameof(ProjectNames))]
    public void EfCoreRuntimePackages_AreReferencedOnlyByInfrastructure(string projectName)
    {
        var project = All[projectName];
        var mayReference = project.Kind == ProjectKind.Infrastructure
            || string.Equals(project.Name, BuildingBlocksInfrastructure, StringComparison.Ordinal);

        var runtimePackages = project.PackageReferences
            .Where(IsDataAccessPackage)
            .Where(p => !DesignTimePackages.Contains(p, StringComparer.Ordinal))
            .ToList();

        Assert.True(
            mayReference || runtimePackages.Count == 0,
            $"{project.Name} ({project.Kind}) references {string.Join(", ", runtimePackages)}. "
            + "EF Core and Npgsql belong in Infrastructure projects only.");
    }

    /// <summary>
    /// EF Core Design/Tools are design-time tooling for migrations and belong in the startup (host) project,
    /// as a private asset so they never flow to anything that references the host.
    /// </summary>
    [Theory]
    [MemberData(nameof(ProjectNames))]
    public void EfCoreDesignTimePackages_AreReferencedOnlyByHosts(string projectName)
    {
        var project = All[projectName];
        var designTime = project.PackageReferences.Where(p => DesignTimePackages.Contains(p, StringComparer.Ordinal));

        Assert.True(
            project.Kind == ProjectKind.Host || !designTime.Any(),
            $"{project.Name} references {string.Join(", ", designTime)}; only host projects may (migrations tooling).");
    }

    /// <summary>
    /// A DbContext may only be declared inside a service's own Infrastructure project. A DbContext in a shared
    /// project (BuildingBlocks, Contracts) would let services share tables.
    /// </summary>
    [Fact]
    public void DbContexts_AreDeclaredOnlyInServiceInfrastructure()
    {
        var violations = Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path => DbContextDeclaration().IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(Root, path).Replace('\\', '/'))
            .Where(path => !ServiceInfrastructurePath().IsMatch(path))
            .ToList();

        Assert.True(
            violations.Count == 0,
            "DbContext declared outside a service Infrastructure project:\n" + string.Join('\n', violations));
    }

    /// <summary>Every service has its own DbContext, so it has its own database and connection string.</summary>
    [Fact]
    public void EveryServiceInfrastructure_DeclaresItsOwnDbContext()
    {
        var missing = OfKind(ProjectKind.Infrastructure)
            .Where(p => !Directory
                .EnumerateFiles(ProjectDirectory(p), "*.cs", SearchOption.AllDirectories)
                .Where(path => !IsBuildOutput(path))
                .Any(path => DbContextDeclaration().IsMatch(File.ReadAllText(path))))
            .Select(p => p.Name)
            .ToList();

        Assert.True(missing.Count == 0, "No DbContext in: " + string.Join(", ", missing));
    }

    /// <summary>
    /// All services share one database for now, so each DbContext must declare its own table prefix, apply
    /// it, and no two services may use the same prefix.
    /// </summary>
    [Fact]
    public void EveryServiceDbContext_AppliesItsOwnUniqueTablePrefix()
    {
        var contexts = OfKind(ProjectKind.Infrastructure)
            .SelectMany(p => Directory
                .EnumerateFiles(ProjectDirectory(p), "*.cs", SearchOption.AllDirectories)
                .Where(path => !IsBuildOutput(path))
                .Select(path => (Project: p.Name, Source: File.ReadAllText(path)))
                .Where(file => DbContextDeclaration().IsMatch(file.Source)))
            .Select(file => (
                file.Project,
                Prefix: TablePrefixDeclaration().Match(file.Source) is { Success: true } m
                    ? m.Groups["prefix"].Value
                    : null,
                Applies: file.Source.Contains("ApplyTablePrefix(TablePrefix)", StringComparison.Ordinal)))
            .ToList();

        Assert.All(contexts, c => Assert.True(
            c.Prefix is not null && c.Applies,
            $"{c.Project}: its DbContext must declare `public const string TablePrefix = \"XYZ_\";` and call "
            + "`modelBuilder.ApplyTablePrefix(TablePrefix)` at the end of OnModelCreating."));

        var duplicates = contexts.GroupBy(c => c.Prefix, StringComparer.Ordinal).Where(g => g.Count() > 1).ToList();
        Assert.True(
            duplicates.Count == 0,
            "Table prefixes must be unique per service: "
            + string.Join(
                "; ",
                duplicates.Select(g => $"{g.Key} used by {string.Join(", ", g.Select(c => c.Project))}")));
    }

    private static bool IsDataAccessPackage(string package) =>
        package.StartsWith(DesignTimePackagePrefix, StringComparison.Ordinal)
        || string.Equals(package, "Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
        || package.StartsWith("Npgsql", StringComparison.Ordinal);

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string ProjectDirectory(ProjectInfo project) =>
        Path.GetDirectoryName(Path.Combine(Root, project.RelativePath))!;

    // "class X : DbContext" or "class X(...) : DbContext(...)", optionally generic or with more base types.
    [GeneratedRegex(@"\bclass\s+\w+(?:<[^>]*>)?(?:\([^)]*\))?\s*:\s*DbContext\b", RegexOptions.None, 1000)]
    private static partial Regex DbContextDeclaration();

    [GeneratedRegex(@"\bconst\s+string\s+TablePrefix\s*=\s*""(?<prefix>[^""]+)""", RegexOptions.None, 1000)]
    private static partial Regex TablePrefixDeclaration();

    [GeneratedRegex(@"^src/Services/(?<service>[^/]+)/\k<service>\.Infrastructure/", RegexOptions.None, 1000)]
    private static partial Regex ServiceInfrastructurePath();
}
