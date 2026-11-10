using BuildingBlocks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace BuildingBlocks.UnitTests.Infrastructure.Persistence;

public sealed class TablePrefixTests
{
    private const string Prefix = "TST_";

    [Fact]
    public void ApplyTablePrefix_PrefixesConventionalAndExplicitTableNames()
    {
        var model = BuildModel();

        Assert.Equal("TST_Product", Table<Product>(model));
        Assert.Equal("TST_Orders", Table<Order>(model));
    }

    [Fact]
    public void ApplyTablePrefix_PrefixesOwnedTypesJoinTablesAndHierarchies()
    {
        var model = BuildModel();

        // Owned type stored in its owner's table (table splitting).
        var address = model.FindEntityType(typeof(Order))!.FindNavigation(nameof(Order.ShippingAddress))!;
        Assert.Equal("TST_Orders", address.TargetEntityType.GetTableName());

        // Many-to-many join table created by convention.
        Assert.Contains(
            model.GetEntityTypes(),
            e => string.Equals(e.GetTableName(), "TST_ProductTag", StringComparison.Ordinal));

        // TPH: base and derived types share one table.
        Assert.Equal("TST_Animal", Table<Animal>(model));
        Assert.Equal("TST_Animal", Table<Dog>(model));
    }

    [Fact]
    public void ApplyTablePrefix_DoesNotPrefixTwice()
    {
        var model = BuildModel();

        Assert.Equal("TST_Audit", Table<AuditEntry>(model));
    }

    [Fact]
    public void ApplyTablePrefix_LeavesViewsAlone()
    {
        var model = BuildModel();
        var summary = model.FindEntityType(typeof(ProductSummary))!;

        Assert.Null(summary.GetTableName());
        Assert.Equal("ProductSummary", summary.GetViewName());
    }

    [Fact]
    public void EveryTableInTheModel_StartsWithThePrefix()
    {
        var model = BuildModel();

        var tables = model.GetEntityTypes().Select(e => e.GetTableName()).OfType<string>()
            .Distinct(StringComparer.Ordinal);

        Assert.All(tables, table => Assert.StartsWith(Prefix, table, StringComparison.Ordinal));
    }

    [Fact]
    public void MigrationsHistoryTable_IsPrefixedPerService() =>
        Assert.Equal("CAT_EFMigrationsHistory", TablePrefix.MigrationsHistoryTable("CAT_"));

    [Theory]
    [InlineData("CAT_")]
    [InlineData("ORD_")]
    [InlineData("NTF_")]
    [InlineData("PMS2_")]
    public void EnsureValid_AcceptsConventionalPrefixes(string prefix) =>
        Assert.Null(Record.Exception(() => TablePrefix.EnsureValid(prefix)));

    [Theory]
    [InlineData("")]
    [InlineData("CAT")]
    [InlineData("cat_")]
    [InlineData("C_")]
    [InlineData("1AB_")]
    [InlineData("CA T_")]
    [InlineData("ABCDEFGHIJK_")]
    public void EnsureValid_RejectsOtherPrefixes(string prefix) =>
        Assert.Throws<ArgumentException>(() => TablePrefix.EnsureValid(prefix));

    private static string? Table<T>(IModel model) => model.FindEntityType(typeof(T))!.GetTableName();

    private static IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<SampleDbContext>()
            .UseNpgsql("Host=localhost;Database=unused") // The model is built without connecting.
            .Options;

        using var context = new SampleDbContext(options);
        return context.Model;
    }

    private sealed class SampleDbContext(DbContextOptions<SampleDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Product>();
            modelBuilder.Entity<Order>(order =>
            {
                order.ToTable("Orders");
                order.OwnsOne(o => o.ShippingAddress);
            });
            modelBuilder.Entity<Product>().HasMany(p => p.Tags).WithMany(t => t.Products);
            modelBuilder.Entity<AuditEntry>().ToTable("TST_Audit");
            modelBuilder.Entity<Animal>();
            modelBuilder.Entity<Dog>();
            modelBuilder.Entity<ProductSummary>().HasNoKey().ToView("ProductSummary");

            modelBuilder.ApplyTablePrefix(Prefix);
        }
    }
}
