using Catalog.Application.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Catalog.UnitTests.Application;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddCatalogApplication_ReturnsSameServiceCollection()
    {
        var services = new ServiceCollection();

        var result = services.AddCatalogApplication();

        Assert.Same(services, result);
    }
}
