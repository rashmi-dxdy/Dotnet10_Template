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
    public void Domain_HasNoPackageReferences(string projectName) =>
        Assert.Empty(All[projectName].PackageReferences);

    [Theory]
    [MemberData(nameof(ServiceProjectNames))]
    public void ServiceProject_ReferencesOnlyAllowedProjects(string projectName)
    {
        var project = All[projectName];
        var allowed = AllowedReferences(project);

        var violations = project.ProjectReferences.Where(r => !allowed.Contains(r)).ToList();
        var message =
            $"{project.Name} ({project.Kind}) has disallowed references: {string.Join(", ", violations)}. "
            + $"Allowed: {string.Join(", ", allowed.Order(StringComparer.Ordinal))}.";

        Assert.True(violations.Count == 0, message);
    }

    [Theory]
    [MemberData(nameof(ServiceProjectNames))]
    public void ServiceProject_DoesNotReferenceAnotherService(string projectName)
    {
        var project = All[projectName];

        var crossService = project.ProjectReferences
            .Where(All.ContainsKey)
            .Select(r => All[r])
            .Where(r => r.Service is not null && !string.Equals(r.Service, project.Service, StringComparison.Ordinal))
            .Select(r => r.Name)
            .ToList();
        var message =
            $"{project.Name} references another service: {string.Join(", ", crossService)}. "
            + "Services must communicate through IntegrationEvents only.";

        Assert.True(crossService.Count == 0, message);
    }

    /// <summary>
    /// Domain and IntegrationEvents depend on BuildingBlocks.Abstractions, so it must stay free of
    /// any technology (EF Core, RabbitMQ, Kafka, ...) and of other projects.
    /// </summary>
    [Fact]
    public void BuildingBlocksAbstractions_HasNoProjectOrPackageReferences()
    {
        var abstractions = All[BuildingBlocksAbstractions];

        Assert.Empty(abstractions.ProjectReferences);
        Assert.Empty(abstractions.PackageReferences);
    }

    /// <summary>
    /// BuildingBlocks.Application carries MediatR, so Domain must never reach it, and it must not
    /// depend on infrastructure or services.
    /// </summary>
    [Fact]
    public void BuildingBlocksApplication_HasNoProjectReferences() =>
        Assert.Empty(All[BuildingBlocksApplication].ProjectReferences);

    /// <summary>MediatR 13+ is commercially licensed; only the Apache-2.0 12.x line is allowed.</summary>
    [Fact]
    public void MediatR_IsPinnedToVersion12()
    {
        var props = System.Xml.Linq.XDocument.Load(Path.Combine(Root, "Directory.Packages.props"));
        var version = props.Descendants("PackageVersion")
            .Where(e => string.Equals((string?)e.Attribute("Include"), "MediatR", StringComparison.Ordinal))
            .Select(e => (string?)e.Attribute("Version"))
            .SingleOrDefault();

        Assert.NotNull(version);
        Assert.StartsWith("12.", version, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildingBlocksInfrastructure_ReferencesOnlyBuildingBlocksAbstractions() =>
        Assert.All(
            All[BuildingBlocksInfrastructure].ProjectReferences,
            r => Assert.Equal(BuildingBlocksAbstractions, r));

    [Fact]
    public void IntegrationEvents_ReferencesOnlyBuildingBlocksAbstractions_AndNoPackages()
    {
        var contracts = All[IntegrationEvents];

        Assert.All(contracts.ProjectReferences, r => Assert.Equal(BuildingBlocksAbstractions, r));
        Assert.Empty(contracts.PackageReferences);
    }

    private static HashSet<string> AllowedReferences(ProjectInfo project)
    {
        var s = project.Service!;

        return project.Kind switch
        {
            ProjectKind.Domain => [BuildingBlocksAbstractions],
            ProjectKind.Application =>
                [$"{s}.Domain", BuildingBlocksAbstractions, BuildingBlocksApplication, IntegrationEvents],
            ProjectKind.Infrastructure =>
            [
                $"{s}.Application",
                $"{s}.Domain",
                BuildingBlocksAbstractions,
                BuildingBlocksApplication,
                BuildingBlocksInfrastructure,
                IntegrationEvents,
            ],
            ProjectKind.Host =>
            [
                $"{s}.Application",
                $"{s}.Infrastructure",
                BuildingBlocksAbstractions,
                BuildingBlocksApplication,
                BuildingBlocksInfrastructure,
            ],
            _ => throw new InvalidOperationException($"{project.Name} is not a service project."),
        };
    }
}
