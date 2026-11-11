namespace Catalog.Contracts;

/// <summary>
/// How other modules address the Catalog module. Its internal API is served at <c>/internal/catalog/...</c>, in this
/// process or in its own (see docs/modules.md).
/// </summary>
public static class CatalogModuleApi
{
    /// <summary>
    /// The module name: its routes (<c>/api/catalog</c>, <c>/internal/catalog</c>), configuration section
    /// (<c>Modules:Catalog</c>), <c>Module</c> log property and message queue / consumer group prefix.
    /// </summary>
    public const string Name = "catalog";
}
