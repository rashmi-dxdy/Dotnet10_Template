using static Architecture.Tests.RepositoryProjects;

namespace Architecture.Tests;

/// <summary>
/// Enforces the allowed project dependency directions.
/// </summary>
public sealed class DependencyRuleTests
{
    public static TheoryData<string> ModuleProjectNames =>
        new(RepositoryProjects.ModuleProjects.Select(p => p.Name).Order(StringComparer.Ordinal));

    public static TheoryData<string> DomainProjectNames =>
        new(OfKind(ProjectKind.Domain).Select(p => p.Name).Order(StringComparer.Ordinal));

    [Fact]
    public void Repository_ContainsExpectedModuleLayers()
    {
        Assert.NotEmpty(OfKind(ProjectKind.Domain));
        Assert.NotEmpty(OfKind(ProjectKind.Application));
        Assert.NotEmpty(OfKind(ProjectKind.Infrastructure));
        Assert.NotEmpty(OfKind(ProjectKind.Api));
        Assert.Single(OfKind(ProjectKind.Host));
    }

    [Theory]
    [MemberData(nameof(DomainProjectNames))]
    public void Domain_HasNoPackageReferences(string projectName) =>
        Assert.Empty(All[projectName].PackageReferences);

    [Theory]
    [MemberData(nameof(ModuleProjectNames))]
    public void ModuleProject_ReferencesOnlyAllowedProjects(string projectName)
    {
        var project = All[projectName];
        var violations = project.ProjectReferences.Where(r => !IsAllowed(project, r)).ToList();
        var message =
            $"{project.Name} ({project.Kind}) has disallowed references: {string.Join(", ", violations)}. "
            + "See the dependency table in docs/architecture.md.";

        Assert.True(violations.Count == 0, message);
    }

    [Theory]
    [MemberData(nameof(ModuleProjectNames))]
    public void ModuleProject_DoesNotReferenceAnotherModule(string projectName)
    {
        var project = All[projectName];

        var crossModule = project.ProjectReferences
            .Where(All.ContainsKey)
            .Select(r => All[r])
            .Where(r => r.Module is not null && !string.Equals(r.Module, project.Module, StringComparison.Ordinal))
            .Select(r => r.Name)
            .ToList();
        var message =
            $"{project.Name} references another module: {string.Join(", ", crossModule)}. Modules talk to each "
            + "other only through internal HTTP APIs (<Module>.Contracts) and IntegrationEvents.";

        Assert.True(crossModule.Count == 0, message);
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

    /// <summary>Shared host setup must not depend on any layer or service.</summary>
    [Fact]
    public void BuildingBlocksHosting_HasNoProjectReferences() =>
        Assert.Empty(All[BuildingBlocksHosting].ProjectReferences);

    [Fact]
    public void BuildingBlocksWeb_ReferencesOnlyBuildingBlocksHostingAndAbstractions() =>
        Assert.All(
            All[BuildingBlocksWeb].ProjectReferences,
            r => Assert.Contains(r, [BuildingBlocksAbstractions, BuildingBlocksHosting], StringComparer.Ordinal));

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

    private static bool IsAllowed(ProjectInfo project, string reference)
    {
        var m = project.Module!;
        var ownContracts = Is(ContractsOf(m));

        return project.Kind switch
        {
            ProjectKind.Domain => reference is BuildingBlocksAbstractions,
            ProjectKind.Application =>
                Is($"{m}.Domain")
                || ownContracts
                || reference is BuildingBlocksAbstractions or BuildingBlocksApplication or IntegrationEvents,

            // Any module's contracts: Infrastructure implements the HTTP clients of other modules' internal APIs.
            ProjectKind.Infrastructure =>
                Is($"{m}.Application")
                || Is($"{m}.Domain")
                || IsModuleContracts(reference)
                || reference is BuildingBlocksAbstractions
                    or BuildingBlocksApplication
                    or BuildingBlocksInfrastructure
                    or IntegrationEvents,
            ProjectKind.Api =>
                Is($"{m}.Application")
                || Is($"{m}.Infrastructure")
                || ownContracts
                || reference is BuildingBlocksAbstractions
                    or BuildingBlocksApplication
                    or BuildingBlocksHosting
                    or BuildingBlocksWeb,
            _ => throw new InvalidOperationException($"{project.Name} is not a module project."),
        };

        bool Is(string name) => string.Equals(reference, name, StringComparison.Ordinal);
    }
}
