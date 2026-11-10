using BuildingBlocks.Hosting.Logging;
using Serilog.Events;
using Serilog.Parsing;

namespace BuildingBlocks.UnitTests.Hosting.Logging;

public sealed class SensitiveDataRedactionEnricherTests
{
    private const string Redacted = SensitiveDataRedactionEnricher.RedactedValue;

    [Theory]
    [InlineData("Password")]
    [InlineData("userPassword")]
    [InlineData("ClientSecret")]
    [InlineData("access_token")]
    [InlineData("ApiKey")]
    [InlineData("ConnectionString")]
    [InlineData("Authorization")]
    public void IsSensitive_ReturnsTrue_ForSensitiveNames(string name) =>
        Assert.True(SensitiveDataRedactionEnricher.IsSensitive(name));

    [Theory]
    [InlineData("UserName")]
    [InlineData("OrderId")]
    [InlineData("CorrelationId")]
    public void IsSensitive_ReturnsFalse_ForOrdinaryNames(string name) =>
        Assert.False(SensitiveDataRedactionEnricher.IsSensitive(name));

    [Fact]
    public void Enrich_RedactsSensitiveTopLevelProperty_AndKeepsOthers()
    {
        var logEvent = CreateEvent(
            new LogEventProperty("Password", new ScalarValue("hunter2")),
            new LogEventProperty("UserName", new ScalarValue("bob")));

        new SensitiveDataRedactionEnricher().Enrich(logEvent, null!);

        Assert.Equal(Redacted, Scalar(logEvent.Properties["Password"]));
        Assert.Equal("bob", Scalar(logEvent.Properties["UserName"]));
    }

    [Fact]
    public void Enrich_RedactsSensitivePropertyInsideDestructuredObject()
    {
        var settings = new StructureValue(
        [
            new LogEventProperty("ConnectionString", new ScalarValue("Host=db;Password=x")),
            new LogEventProperty("Region", new ScalarValue("eu")),
        ]);
        var logEvent = CreateEvent(new LogEventProperty("Settings", settings));

        new SensitiveDataRedactionEnricher().Enrich(logEvent, null!);

        var redacted = Assert.IsType<StructureValue>(logEvent.Properties["Settings"]);
        Assert.Equal(Redacted, Scalar(Property(redacted, "ConnectionString")));
        Assert.Equal("eu", Scalar(Property(redacted, "Region")));
    }

    [Fact]
    public void Enrich_RedactsSensitiveDictionaryKey_InsideSequence()
    {
        var dictionary = new DictionaryValue(
        [
            new KeyValuePair<ScalarValue, LogEventPropertyValue>(new ScalarValue("api_key"), new ScalarValue("k")),
            new KeyValuePair<ScalarValue, LogEventPropertyValue>(new ScalarValue("name"), new ScalarValue("n")),
        ]);
        var logEvent = CreateEvent(new LogEventProperty("Items", new SequenceValue([dictionary])));

        new SensitiveDataRedactionEnricher().Enrich(logEvent, null!);

        var sequence = Assert.IsType<SequenceValue>(logEvent.Properties["Items"]);
        var elements = Assert.IsType<DictionaryValue>(Assert.Single(sequence.Elements)).Elements;
        Assert.Equal(Redacted, Scalar(elements.Single(e => Equals(e.Key.Value, "api_key")).Value));
        Assert.Equal("n", Scalar(elements.Single(e => Equals(e.Key.Value, "name")).Value));
    }

    [Fact]
    public void Enrich_LeavesEventWithoutSensitiveDataUnchanged()
    {
        var value = new StructureValue([new LogEventProperty("Region", new ScalarValue("eu"))]);
        var logEvent = CreateEvent(new LogEventProperty("Settings", value));

        new SensitiveDataRedactionEnricher().Enrich(logEvent, null!);

        Assert.Same(value, logEvent.Properties["Settings"]);
    }

    private static object? Scalar(LogEventPropertyValue value) => Assert.IsType<ScalarValue>(value).Value;

    private static LogEventPropertyValue Property(StructureValue structure, string name) =>
        structure.Properties.Single(p => string.Equals(p.Name, name, StringComparison.Ordinal)).Value;

    private static LogEvent CreateEvent(params LogEventProperty[] properties) =>
        new(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            exception: null,
            new MessageTemplateParser().Parse("test"),
            properties);
}
