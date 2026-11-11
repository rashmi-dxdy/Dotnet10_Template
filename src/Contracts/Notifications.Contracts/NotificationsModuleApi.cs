namespace Notifications.Contracts;

/// <summary>
/// How other modules address the Notifications module. Its internal API is served at <c>/internal/notifications/...</c>, in this
/// process or in its own (see docs/modules.md).
/// </summary>
public static class NotificationsModuleApi
{
    /// <summary>
    /// The module name: its routes (<c>/api/notifications</c>, <c>/internal/notifications</c>), configuration section
    /// (<c>Modules:Notifications</c>), <c>Module</c> log property and message queue / consumer group prefix.
    /// </summary>
    public const string Name = "notifications";
}
