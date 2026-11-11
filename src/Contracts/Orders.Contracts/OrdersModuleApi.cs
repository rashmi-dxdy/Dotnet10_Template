namespace Orders.Contracts;

/// <summary>
/// How other modules address the Orders module. Its internal API is served at <c>/internal/orders/...</c>, in this
/// process or in its own (see docs/modules.md).
/// </summary>
public static class OrdersModuleApi
{
    /// <summary>
    /// The module name: its routes (<c>/api/orders</c>, <c>/internal/orders</c>), configuration section
    /// (<c>Modules:Orders</c>), <c>Module</c> log property and message queue / consumer group prefix.
    /// </summary>
    public const string Name = "orders";
}
