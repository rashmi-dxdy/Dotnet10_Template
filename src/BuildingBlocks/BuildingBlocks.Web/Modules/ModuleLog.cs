using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Web.Modules;

/// <summary>High-performance log messages for module hosting.</summary>
internal static partial class ModuleLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Modules hosted in this process: {HostedModules}. Internal API access: {InternalApiAccess}")]
    public static partial void Hosting(ILogger logger, string hostedModules, string internalApiAccess);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "Modules NOT hosted in this process (called at Modules:<Name>:BaseAddress): {NotHostedModules}")]
    public static partial void NotHosting(ILogger logger, string notHostedModules);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Rejected internal API request {Path} from {RemoteAddress}: missing or wrong internal API key")]
    public static partial void InternalApiRejected(ILogger logger, string path, string? remoteAddress);
}
