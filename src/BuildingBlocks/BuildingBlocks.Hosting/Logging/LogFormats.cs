namespace BuildingBlocks.Hosting.Logging;

/// <summary>
/// Serilog.Expressions templates shared by every host. The same templates are set in each service's
/// <c>appsettings*.json</c> (<c>Serilog:WriteTo:Console:Args:formatter:template</c>); keep them identical.
/// </summary>
public static class LogFormats
{
    /// <summary>
    /// One JSON object per line, using the CLEF property names (<c>@t</c> UTC timestamp, <c>@l</c> level,
    /// <c>@m</c> rendered message, <c>@mt</c> template, <c>@x</c> exception, <c>@tr</c>/<c>@sp</c> trace and
    /// span IDs) followed by every other property. Used for appsettings.json defaults (Docker, production)
    /// and by the bootstrap logger.
    /// </summary>
    public const string Json = "{ {@t: UtcDateTime(@t), @l, @m, @mt, @x, @tr, @sp, ..@p} }\n";
}
