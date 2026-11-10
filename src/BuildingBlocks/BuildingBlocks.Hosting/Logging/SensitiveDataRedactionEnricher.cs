using Serilog.Core;
using Serilog.Events;

namespace BuildingBlocks.Hosting.Logging;

/// <summary>
/// Safety net that replaces the value of any log property whose name looks sensitive (password, secret,
/// token, connection string, ...) with <see cref="RedactedValue"/>. Properties nested inside destructured
/// objects, dictionaries and sequences are redacted too.
/// </summary>
/// <remarks>
/// It only catches values logged under a recognisable name. Never log request or response bodies,
/// configuration sections or whole options objects in the first place.
/// </remarks>
public sealed class SensitiveDataRedactionEnricher : ILogEventEnricher
{
    /// <summary>The text that replaces a sensitive value.</summary>
    public const string RedactedValue = "***REDACTED***";

    private static readonly ScalarValue Redacted = new(RedactedValue);

    // Matched case-insensitively as substrings of the property name.
    private static readonly string[] SensitiveNameParts =
    [
        "password",
        "passwd",
        "pwd",
        "secret",
        "token",
        "apikey",
        "api_key",
        "connectionstring",
        "authorization",
        "cookie",
        "credential",
        "privatekey",
        "private_key",
    ];

    /// <summary>Returns whether a property name looks like it holds sensitive data.</summary>
    /// <param name="name">The property name.</param>
    /// <returns><see langword="true"/> when the value should be redacted.</returns>
    public static bool IsSensitive(string name) =>
        SensitiveNameParts.Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        List<LogEventProperty>? updates = null;
        foreach (var (name, value) in logEvent.Properties)
        {
            var redacted = IsSensitive(name) ? Redacted : Redact(value);
            if (!ReferenceEquals(redacted, value))
            {
                (updates ??= []).Add(new LogEventProperty(name, redacted));
            }
        }

        updates?.ForEach(logEvent.AddOrUpdateProperty);
    }

    private static LogEventPropertyValue Redact(LogEventPropertyValue value) => value switch
    {
        StructureValue structure => RedactStructure(structure),
        DictionaryValue dictionary => RedactDictionary(dictionary),
        SequenceValue sequence => RedactSequence(sequence),
        _ => value,
    };

    private static StructureValue RedactStructure(StructureValue structure)
    {
        var changed = false;
        var properties = new List<LogEventProperty>();
        foreach (var property in structure.Properties)
        {
            var redacted = IsSensitive(property.Name) ? Redacted : Redact(property.Value);
            changed |= !ReferenceEquals(redacted, property.Value);
            properties.Add(new LogEventProperty(property.Name, redacted));
        }

        return changed ? new StructureValue(properties, structure.TypeTag) : structure;
    }

    private static DictionaryValue RedactDictionary(DictionaryValue dictionary)
    {
        var changed = false;
        var elements = new List<KeyValuePair<ScalarValue, LogEventPropertyValue>>();
        foreach (var (key, value) in dictionary.Elements)
        {
            var redacted = key.Value is string name && IsSensitive(name) ? Redacted : Redact(value);
            changed |= !ReferenceEquals(redacted, value);
            elements.Add(new KeyValuePair<ScalarValue, LogEventPropertyValue>(key, redacted));
        }

        return changed ? new DictionaryValue(elements) : dictionary;
    }

    private static SequenceValue RedactSequence(SequenceValue sequence)
    {
        var changed = false;
        var elements = new List<LogEventPropertyValue>();
        foreach (var element in sequence.Elements)
        {
            var redacted = Redact(element);
            changed |= !ReferenceEquals(redacted, element);
            elements.Add(redacted);
        }

        return changed ? new SequenceValue(elements) : sequence;
    }
}
