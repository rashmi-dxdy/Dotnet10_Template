using static Architecture.Tests.RepositoryProjects;

namespace Architecture.Tests;

/// <summary>
/// Keeps the message broker client inside Infrastructure. Domain and Application use the technology-agnostic
/// abstractions in BuildingBlocks.Abstractions.Messaging. See docs/messaging.md.
/// </summary>
public sealed class MessagingRuleTests
{
    private static readonly string[] BrokerClientPackages = ["RabbitMQ.Client", "Confluent.Kafka"];

    public static TheoryData<string> SourceProjectNames =>
        new(All.Values.Where(p => p.Kind != ProjectKind.Test).Select(p => p.Name).Order(StringComparer.Ordinal));

    [Theory]
    [MemberData(nameof(SourceProjectNames))]
    public void BrokerClients_AreReferencedOnlyByInfrastructure(string projectName)
    {
        var project = All[projectName];
        var mayReference = project.Kind == ProjectKind.Infrastructure
            || string.Equals(project.Name, BuildingBlocksInfrastructure, StringComparison.Ordinal);

        var brokerClients = project.PackageReferences
            .Where(p => BrokerClientPackages.Contains(p, StringComparer.Ordinal))
            .ToList();

        Assert.True(
            mayReference || brokerClients.Count == 0,
            $"{project.Name} ({project.Kind}) references {string.Join(", ", brokerClients)}. Broker clients belong in "
            + "Infrastructure; use IMessagePublisher / IMessageHandler<T> from BuildingBlocks.Abstractions.Messaging.");
    }

    /// <summary>Domain and Application code must not use a broker client API (RabbitMQ, Kafka), even transitively.</summary>
    [Fact]
    public void DomainAndApplicationSources_DoNotUseBrokerClients()
    {
        var violations = All.Values
            .Where(p => p.Kind is ProjectKind.Domain or ProjectKind.Application
                || string.Equals(p.Name, BuildingBlocksAbstractions, StringComparison.Ordinal)
                || string.Equals(p.Name, BuildingBlocksApplication, StringComparison.Ordinal))
            .SelectMany(p => Directory.EnumerateFiles(
                Path.GetDirectoryName(Path.Combine(Root, p.RelativePath))!,
                "*.cs",
                SearchOption.AllDirectories))
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path) is var source
                && (source.Contains("using RabbitMQ", StringComparison.Ordinal)
                    || source.Contains("using Confluent", StringComparison.Ordinal)))
            .Select(path => Path.GetRelativePath(Root, path))
            .ToList();

        Assert.True(
            violations.Count == 0,
            "RabbitMQ.Client used outside Infrastructure:\n" + string.Join('\n', violations));
    }
}
