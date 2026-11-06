using System.Xml.Linq;
using static Architecture.Tests.RepositoryProjects;

namespace Architecture.Tests;

/// <summary>
/// Checks the overall project graph: every reference resolves, there are no cycles, and every
/// project is part of the solution.
/// </summary>
public sealed class SolutionIntegrityTests
{
    [Fact]
    public void AllProjectReferences_Resolve()
    {
        var unresolved = All.Values
            .SelectMany(p => p.UnresolvedProjectReferences.Select(r => $"{p.Name} -> {r}"))
            .ToList();

        Assert.True(unresolved.Count == 0, "Unresolved project references:\n" + string.Join('\n', unresolved));
    }

    [Fact]
    public void ProjectGraph_HasNoCycles()
    {
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var path = new Stack<string>();

        foreach (var project in All.Keys)
        {
            var cycle = FindCycle(project, visiting, visited, path);
            Assert.True(cycle is null, $"Circular project reference: {cycle}");
        }
    }

    [Fact]
    public void AllProjects_AreInSolution()
    {
        var solution = XDocument.Load(Path.Combine(Root, SolutionFileName));
        var inSolution = solution.Descendants("Project")
            .Select(e => ((string?)e.Attribute("Path") ?? string.Empty).Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = All.Values
            .Select(p => p.RelativePath)
            .Where(p => !inSolution.Contains(p))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0, "Projects missing from the solution:\n" + string.Join('\n', missing));
    }

    private static string? FindCycle(
        string project,
        HashSet<string> visiting,
        HashSet<string> visited,
        Stack<string> path)
    {
        if (visited.Contains(project))
        {
            return null;
        }

        if (!visiting.Add(project))
        {
            var chain = path.Reverse().SkipWhile(p => !string.Equals(p, project, StringComparison.Ordinal)).Append(project);
            return string.Join(" -> ", chain);
        }

        path.Push(project);

        foreach (var reference in All[project].ProjectReferences.Where(All.ContainsKey))
        {
            var cycle = FindCycle(reference, visiting, visited, path);
            if (cycle is not null)
            {
                return cycle;
            }
        }

        path.Pop();
        visiting.Remove(project);
        visited.Add(project);

        return null;
    }
}
