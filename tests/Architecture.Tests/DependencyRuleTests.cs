using static Architecture.Tests.RepositoryProjects;

namespace Architecture.Tests;

/// <summary>
/// Enforces the allowed project dependency directions.
/// </summary>
public sealed class DependencyRuleTests
{
    public static TheoryData<string> ServiceProjectNames =>
        new(RepositoryProjects.ServiceProjects.Select(p => p.Name).Order(StringComparer.Ordinal));

    public static TheoryData<string> DomainProjectNames =>
        new(OfKind(ProjectKind.Domain).Select(p => p.Name).Order(StringComparer.Ordinal));

    [Fact]
    public void Repository_ContainsExpectedServiceLayers()
    {
        Assert.NotEmpty(OfKind(ProjectKind.Domain));
        Assert.NotEmpty(OfKind(ProjectKind.Application));
        Assert.NotEmpty(OfKind(ProjectKind.Infrastructure));
        Assert.NotEmpty(OfKind(ProjectKind.Host));
    }

    [Theory]
    [MemberData(nameof(DomainProjectNames))]
    public void Domain_HasNoProjectOrPackageReferences(string projectName)
    {
        var project = All[projectName];

        Assert.Empty(project.ProjectReferences);
        Assert.Empty(project.PackageReferences);
    }

    [Theory]
    [MemberData(nameof(ServiceProjectNames))]
    public void ServiceProject_ReferencesOnlyAllowedProjects(string projectName)
    {
        var project = All[projectName];
        var allowed = AllowedReferences(project);

        var violations = project.ProjectReferences.Where(r => !allowed.Contains(r)).ToList();

        Assert.True(
            violations.Count == 0,
            $"{project.Name} ({project.Kind}) has disallowed references: {string.Join(", ", violations)}. "
            + $"Allowed: {string.Join(", ", allowed.Order(StringComparer.Ordinal))}.");
    }

    [Theory]
    [MemberData(nameof(ServiceProjectNames))]
    public void ServiceProject_DoesNotReferenceAnotherService(string projectName)
    {
        var project = All[projectName];

        var crossService = project.ProjectReferences
            .Where(All.ContainsKey)
            .Select(r => All[r])
            .Where(r => r.Service is not null && r.Service != project.Service)
            .Select(r => r.Name)
            .ToList();

        Assert.True(
            crossService.Count == 0,
            $"{project.Name} references another service: {string.Join(", ", crossService)}. "
            + "Services must communicate through IntegrationEvents only.");
    }

    [Fact]
    public void BuildingBlocksAbstractions_HasNoProjectReferences() =>
        Assert.Empty(All[BuildingBlocksAbstractions].ProjectReferences);

    [Fact]
    public void BuildingBlocksInfrastructure_ReferencesOnlyBuildingBlocksAbstractions() =>
        Assert.All(
            All[BuildingBlocksInfrastructure].ProjectReferences,
            r => Assert.Equal(BuildingBlocksAbstractions, r));

    [Fact]
    public void IntegrationEvents_HasNoProjectOrPackageReferences()
    {
        var contracts = All[IntegrationEvents];

        Assert.Empty(contracts.ProjectReferences);
        Assert.Empty(contracts.PackageReferences);
    }

    private static HashSet<string> AllowedReferences(ProjectInfo project)
    {
        var s = project.Service!;

        return project.Kind switch
        {
            ProjectKind.Domain => [],
            ProjectKind.Application => [$"{s}.Domain", BuildingBlocksAbstractions, IntegrationEvents],
            ProjectKind.Infrastructure =>
            [
                $"{s}.Application",
                $"{s}.Domain",
                BuildingBlocksAbstractions,
                BuildingBlocksInfrastructure,
                IntegrationEvents,
            ],
            ProjectKind.Host =>
            [
                $"{s}.Application",
                $"{s}.Infrastructure",
                BuildingBlocksAbstractions,
                BuildingBlocksInfrastructure,
            ],
            _ => throw new InvalidOperationException($"{project.Name} is not a service project."),
        };
    }
}
