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
    Host,
    Test,
}

/// <summary>A project file and the references it declares.</summary>
internal sealed record ProjectInfo(
    string Name,
    string RelativePath,
    ProjectKind Kind,
    string? Service,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> UnresolvedProjectReferences,
    IReadOnlyList<string> PackageReferences);

/// <summary>Discovers and parses every project in the repository.</summary>
internal static class RepositoryProjects
{
    public const string SolutionFileName = "Dotnet10Template.slnx";
    public const string BuildingBlocksAbstractions = "BuildingBlocks.Abstractions";
    public const string BuildingBlocksApplication = "BuildingBlocks.Application";
    public const string BuildingBlocksInfrastructure = "BuildingBlocks.Infrastructure";
    public const string IntegrationEvents = "IntegrationEvents";

    private static readonly string[] ProjectFolders = ["src", "tests"];
    private static readonly Lazy<string> LazyRoot = new(FindRoot);
    private static readonly Lazy<IReadOnlyDictionary<string, ProjectInfo>> LazyProjects = new(LoadAll);

    public static string Root => LazyRoot.Value;

    /// <summary>Gets all projects under <c>src</c> and <c>tests</c>, keyed by project name.</summary>
    public static IReadOnlyDictionary<string, ProjectInfo> All => LazyProjects.Value;

    public static IEnumerable<ProjectInfo> ServiceProjects => All.Values.Where(p => p.Service is not null);

    public static IEnumerable<ProjectInfo> OfKind(ProjectKind kind) => All.Values.Where(p => p.Kind == kind);

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
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.csproj", SearchOption.AllDirectories))
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

        var (kind, service) = Classify(name, relativePath);

        return new ProjectInfo(
            name,
            relativePath,
            kind,
            service,
            referencePaths.Select(Path.GetFileNameWithoutExtension).Select(n => n!).ToList(),
            unresolved,
            packages);
    }

    private static (ProjectKind Kind, string? Service) Classify(string name, string relativePath)
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

        // src/Services/{Service}/{Service}.{Layer}/{Service}.{Layer}.csproj
        var segments = relativePath.Split('/');
        if (segments.Length == 5 && string.Equals(segments[1], "Services", StringComparison.Ordinal))
        {
            var service = segments[2];
            var layer = name.StartsWith(service + ".", StringComparison.Ordinal)
                ? name[(service.Length + 1)..]
                : throw new InvalidOperationException(
                    $"Project '{name}' in service folder '{service}' must be named '{service}.<Layer>'.");

            var kind = layer switch
            {
                "Domain" => ProjectKind.Domain,
                "Application" => ProjectKind.Application,
                "Infrastructure" => ProjectKind.Infrastructure,
                "Api" or "Worker" => ProjectKind.Host,
                _ => throw new InvalidOperationException($"Project '{name}' has an unknown layer '{layer}'."),
            };

            return (kind, service);
        }

        throw new InvalidOperationException($"Project '{relativePath}' is outside the known folder structure.");
    }

    private static string ToForwardSlashes(string path) => path.Replace('\\', '/');
}
