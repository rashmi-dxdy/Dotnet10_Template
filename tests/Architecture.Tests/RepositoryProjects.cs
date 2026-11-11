using System.Xml.Linq;

namespace Architecture.Tests;

/// <summary>The architectural role of a project, derived from its location and name.</summary>
internal enum ProjectKind
{
    BuildingBlock,
    Contracts,
    Domain,
    Application,
    Infrastructure,

    /// <summary>A module's entry point and HTTP surface (<c>&lt;Module&gt;.Api</c>), a class library.</summary>
    Api,

    /// <summary>The deployable application that composes the modules (<c>src/Host</c>).</summary>
    Host,
    Test,
}

/// <summary>A project file and the references it declares.</summary>
internal sealed record ProjectInfo(
    string Name,
    string RelativePath,
    ProjectKind Kind,
    string? Module,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> UnresolvedProjectReferences,
    IReadOnlyList<string> PackageReferences)
{
    public string Directory => Path.GetDirectoryName(Path.Combine(RepositoryProjects.Root, RelativePath))!;
}

/// <summary>Discovers and parses every project in the repository.</summary>
internal static class RepositoryProjects
{
    public const string SolutionFileName = "Dotnet10Template.slnx";
    public const string BuildingBlocksAbstractions = "BuildingBlocks.Abstractions";
    public const string BuildingBlocksApplication = "BuildingBlocks.Application";
    public const string BuildingBlocksHosting = "BuildingBlocks.Hosting";
    public const string BuildingBlocksInfrastructure = "BuildingBlocks.Infrastructure";
    public const string BuildingBlocksWeb = "BuildingBlocks.Web";
    public const string IntegrationEvents = "IntegrationEvents";
    public const string ContractsSuffix = ".Contracts";

    private static readonly string[] ProjectFolders = ["src", "tests"];
    private static readonly Lazy<string> LazyRoot = new(FindRoot);
    private static readonly Lazy<IReadOnlyDictionary<string, ProjectInfo>> LazyProjects = new(LoadAll);

    public static string Root => LazyRoot.Value;

    /// <summary>Gets all projects under <c>src</c> and <c>tests</c>, keyed by project name.</summary>
    public static IReadOnlyDictionary<string, ProjectInfo> All => LazyProjects.Value;

    /// <summary>Gets the layer projects of every module (Domain, Application, Infrastructure, Api).</summary>
    public static IEnumerable<ProjectInfo> ModuleProjects => All.Values.Where(p => p.Module is not null);

    /// <summary>Gets the module names, from <c>src/Modules/&lt;Module&gt;</c>.</summary>
    public static IReadOnlyList<string> Modules =>
        ModuleProjects.Select(p => p.Module!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

    public static IEnumerable<ProjectInfo> OfKind(ProjectKind kind) => All.Values.Where(p => p.Kind == kind);

    /// <summary>The contracts project other modules use to talk to <paramref name="module"/>.</summary>
    public static string ContractsOf(string module) => module + ContractsSuffix;

    /// <summary>Whether <paramref name="projectName"/> is a module's contracts project (not IntegrationEvents).</summary>
    public static bool IsModuleContracts(string projectName) =>
        All.TryGetValue(projectName, out var project)
        && project.Kind == ProjectKind.Contracts
        && projectName.EndsWith(ContractsSuffix, StringComparison.Ordinal);

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Could not find {SolutionFileName} in any parent of {AppContext.BaseDirectory}.");
    }

    private static Dictionary<string, ProjectInfo> LoadAll() =>
        ProjectFolders
            .Select(folder => Path.Combine(Root, folder))
            .Where(System.IO.Directory.Exists)
            .SelectMany(folder => System.IO.Directory.EnumerateFiles(folder, "*.csproj", SearchOption.AllDirectories))
            .Select(Load)
            .ToDictionary(p => p.Name, StringComparer.Ordinal);

    private static ProjectInfo Load(string fullPath)
    {
        var document = XDocument.Load(fullPath);
        var projectDirectory = Path.GetDirectoryName(fullPath)!;
        var relativePath = ToForwardSlashes(Path.GetRelativePath(Root, fullPath));
        var name = Path.GetFileNameWithoutExtension(fullPath);

        var referencePaths = document.Descendants("ProjectReference")
            .Select(e => ToForwardSlashes((string?)e.Attribute("Include") ?? string.Empty))
            .ToList();

        var packages = document.Descendants("PackageReference")
            .Select(e => (string?)e.Attribute("Include") ?? string.Empty)
            .ToList();

        var unresolved = referencePaths
            .Where(path => !File.Exists(Path.GetFullPath(Path.Combine(projectDirectory, path))))
            .ToList();

        var (kind, module) = Classify(name, relativePath);

        return new ProjectInfo(
            name,
            relativePath,
            kind,
            module,
            referencePaths.Select(Path.GetFileNameWithoutExtension).Select(n => n!).ToList(),
            unresolved,
            packages);
    }

    private static (ProjectKind Kind, string? Module) Classify(string name, string relativePath)
    {
        if (relativePath.StartsWith("tests/", StringComparison.Ordinal))
        {
            return (ProjectKind.Test, null);
        }

        if (relativePath.StartsWith("src/BuildingBlocks/", StringComparison.Ordinal))
        {
            return (ProjectKind.BuildingBlock, null);
        }

        if (relativePath.StartsWith("src/Contracts/", StringComparison.Ordinal))
        {
            return (ProjectKind.Contracts, null);
        }

        if (relativePath.StartsWith("src/Host/", StringComparison.Ordinal))
        {
            return (ProjectKind.Host, null);
        }

        // src/Modules/{Module}/{Module}.{Layer}/{Module}.{Layer}.csproj
        var segments = relativePath.Split('/');
        if (segments.Length == 5 && string.Equals(segments[1], "Modules", StringComparison.Ordinal))
        {
            var module = segments[2];
            var layer = name.StartsWith(module + ".", StringComparison.Ordinal)
                ? name[(module.Length + 1)..]
                : throw new InvalidOperationException(
                    $"Project '{name}' in module folder '{module}' must be named '{module}.<Layer>'.");

            var kind = layer switch
            {
                "Domain" => ProjectKind.Domain,
                "Application" => ProjectKind.Application,
                "Infrastructure" => ProjectKind.Infrastructure,
                "Api" => ProjectKind.Api,
                _ => throw new InvalidOperationException(
                    $"Project '{name}' has an unknown layer '{layer}' (Domain, Application, Infrastructure, Api)."),
            };

            return (kind, module);
        }

        throw new InvalidOperationException($"Project '{relativePath}' is outside the known folder structure.");
    }

    private static string ToForwardSlashes(string path) => path.Replace('\\', '/');
}
