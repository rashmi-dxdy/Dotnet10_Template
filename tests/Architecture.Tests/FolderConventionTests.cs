using static Architecture.Tests.RepositoryProjects;

namespace Architecture.Tests;

/// <summary>
/// Enforces the DDD/CQRS folder conventions in every service layer project.
/// See docs/architecture.md.
/// </summary>
public sealed class FolderConventionTests
{
    private static readonly Dictionary<ProjectKind, string[]> RequiredFolders = new()
    {
        [ProjectKind.Domain] = ["Aggregates", "Entities", "Events", "Exceptions", "Repositories", "ValueObjects"],
        [ProjectKind.Application] =
            ["Abstractions", "Behaviors", "Commands", "DependencyInjection", "Queries", "Validators"],
        [ProjectKind.Infrastructure] = ["DependencyInjection", "ExternalServices", "Messaging", "Persistence"],
    };

    public static TheoryData<string> LayerProjectNames =>
        new(RepositoryProjects.ServiceProjects
            .Where(p => RequiredFolders.ContainsKey(p.Kind))
            .Select(p => p.Name)
            .Order(StringComparer.Ordinal));

    [Theory]
    [MemberData(nameof(LayerProjectNames))]
    public void LayerProject_HasConventionFolders(string projectName)
    {
        var project = All[projectName];
        var projectDirectory = Path.GetDirectoryName(Path.Combine(Root, project.RelativePath))!;

        var missing = RequiredFolders[project.Kind]
            .Where(folder => !Directory.Exists(Path.Combine(projectDirectory, folder)))
            .ToList();
        var message = $"{project.Name} ({project.Kind}) is missing folders: {string.Join(", ", missing)}.";

        Assert.True(missing.Count == 0, message);
    }
}
