namespace BuildingBlocks.UnitTests.Infrastructure.Persistence;

// Sample entities for TablePrefixTests, one per kind of table mapping. Internal (not private nested) on purpose:
// the "remove unused private member" code fix applied by `dotnet format` would delete properties EF Core needs.

internal sealed class Product
{
    public int Id { get; set; }

    public List<Tag> Tags { get; set; } = [];
}

internal sealed class Tag
{
    public int Id { get; set; }

    public List<Product> Products { get; set; } = [];
}

internal sealed class Order
{
    public int Id { get; set; }

    public Address ShippingAddress { get; set; } = new();
}

internal sealed class Address
{
    public string City { get; set; } = string.Empty;
}

internal sealed class AuditEntry
{
    public int Id { get; set; }
}

internal class Animal
{
    public int Id { get; set; }
}

internal sealed class Dog : Animal
{
    public string Name { get; set; } = string.Empty;
}

internal sealed class ProductSummary
{
    public int ProductCount { get; set; }
}

