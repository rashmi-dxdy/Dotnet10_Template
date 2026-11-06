using Microsoft.Extensions.DependencyInjection;
using Orders.Application;

namespace Orders.UnitTests.Application;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddOrdersApplication_ReturnsSameServiceCollection()
    {
        var services = new ServiceCollection();

        var result = services.AddOrdersApplication();

        Assert.Same(services, result);
    }
}
