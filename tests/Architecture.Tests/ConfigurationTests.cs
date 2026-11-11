using System.Text.Json;
using System.Text.Json.Nodes;
using static Architecture.Tests.RepositoryProjects;

namespace Architecture.Tests;

/// <summary>
/// Keeps the host configuration complete for every module, consistent and free of secrets.
/// See docs/logging.md and docs/modules.md.
/// </summary>
public sealed class ConfigurationTests
{
    private static readonly string[] SettingsFiles = ["appsettings.json", "appsettings.Development.json"];

    // Keys whose values must never be committed. Matched case-insensitively as substrings of the key.
    private static readonly string[] SecretKeyParts =
        ["password", "secret", "token", "apikey", "api_key", "connectionstring", "credential", "privatekey"];

    public static TheoryData<string> HostNames =>
        new(OfKind(ProjectKind.Host).Select(p => p.Name).Order(StringComparer.Ordinal));

    public static TheoryData<string> SettingsFileNames => new(SettingsFiles);

    [Theory]
    [MemberData(nameof(HostNames))]
    public void Host_HasSettingsFiles(string hostName)
    {
        var missing = SettingsFiles.Where(f => !File.Exists(SettingsPath(hostName, f))).ToList();

        Assert.True(missing.Count == 0, $"{hostName} is missing: {string.Join(", ", missing)}.");
    }

    /// <summary>Every service logs with the same levels, sink and format.</summary>
    [Theory]
    [MemberData(nameof(SettingsFileNames))]
    public void SerilogSection_IsIdenticalInEveryHost(string fileName)
    {
        var sections = OfKind(ProjectKind.Host)
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => (p.Name, Serilog: Load(p.Name, fileName)["Serilog"]?.ToJsonString()))
            .ToList();

        Assert.All(sections, s => Assert.False(s.Serilog is null, $"{s.Name}/{fileName} has no Serilog section."));

        var reference = sections[0];
        Assert.All(
            sections,
            s => Assert.True(
                string.Equals(s.Serilog, reference.Serilog, StringComparison.Ordinal),
                $"The Serilog section of {s.Name}/{fileName} differs from {reference.Name}/{fileName}. "
                + "Keep it identical in every host."));
    }

    /// <summary>
    /// Serilog replaces Microsoft.Extensions.Logging filtering, so a "Logging" section would be silently
    /// ignored and mislead readers. Levels belong in "Serilog:MinimumLevel".
    /// </summary>
    [Theory]
    [MemberData(nameof(HostNames))]
    public void Settings_HaveNoLoggingSection(string hostName) =>
        Assert.All(SettingsFiles, f => Assert.Null(Load(hostName, f)["Logging"]));

    /// <summary>
    /// Committed settings may name secrets but never contain them. Real values come from user secrets
    /// (development) or environment variables (Docker).
    /// </summary>
    [Theory]
    [MemberData(nameof(HostNames))]
    public void Settings_ContainNoSecretValues(string hostName)
    {
        var leaks = SettingsFiles
            .SelectMany(f => Values(Load(hostName, f), f))
            .Where(v => IsSecretKey(v.Path) && !string.IsNullOrEmpty(v.Value))
            .Select(v => v.Path)
            .ToList();

        Assert.True(leaks.Count == 0, $"{hostName} commits secret values at: {string.Join(", ", leaks)}.");
    }

    /// <summary>
    /// The host runs every module, so its base settings must say where each module runs (<c>Modules:&lt;Module&gt;</c>)
    /// and name each module's connection string, even while all of them point at one shared database.
    /// </summary>
    [Fact]
    public void HostSettings_ConfigureEveryModule()
    {
        var host = Assert.Single(OfKind(ProjectKind.Host)).Name;
        var settings = Load(host, "appsettings.json");

        var missing = Modules
            .SelectMany(m => new[]
            {
                settings["Modules"]?[m]?["Enabled"] is null ? $"Modules:{m}:Enabled" : null,
                settings["Modules"]?[m]?["BaseAddress"] is null ? $"Modules:{m}:BaseAddress" : null,
                settings["ConnectionStrings"]?[$"{m}Db"] is null ? $"ConnectionStrings:{m}Db" : null,
            })
            .OfType<string>()
            .ToList();

        Assert.True(missing.Count == 0, $"{host}/appsettings.json is missing: {string.Join(", ", missing)}.");
    }

    private static bool IsSecretKey(string path) =>
        SecretKeyParts.Any(part => path.Contains(part, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<(string Path, string? Value)> Values(JsonNode? node, string path) => node switch
    {
        JsonObject obj => obj.SelectMany(p => Values(p.Value, $"{path}:{p.Key}")),
        JsonArray array => array.SelectMany((item, i) => Values(item, $"{path}:{i}")),
        JsonValue value => [(path, value.ToString())],
        _ => [],
    };

    private static JsonNode Load(string hostName, string fileName) =>
        JsonNode.Parse(
            File.ReadAllText(SettingsPath(hostName, fileName)),
            documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip })
        ?? throw new InvalidOperationException($"{hostName}/{fileName} is empty.");

    private static string SettingsPath(string hostName, string fileName)
    {
        return Path.Combine(All[hostName].Directory, fileName);
    }
}
