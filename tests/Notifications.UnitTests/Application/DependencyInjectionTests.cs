using Microsoft.Extensions.DependencyInjection;
using Notifications.Application;

namespace Notifications.UnitTests.Application;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddNotificationsApplication_ReturnsSameServiceCollection()
    {
        var services = new ServiceCollection();

        var result = services.AddNotificationsApplication();

        Assert.Same(services, result);
    }
}
