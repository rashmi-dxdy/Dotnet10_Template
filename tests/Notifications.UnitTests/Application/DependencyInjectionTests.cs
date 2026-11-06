using Notifications.Application;
using Microsoft.Extensions.DependencyInjection;

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
