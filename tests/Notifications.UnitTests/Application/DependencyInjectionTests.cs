using BuildingBlocks.Application.Behaviors;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Application.DependencyInjection;

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

    [Fact]
    public void AddNotificationsApplication_RegistersMediatR()
    {
        var services = new ServiceCollection();

        services.AddNotificationsApplication();

        Assert.Contains(services, d => d.ServiceType == typeof(ISender));
        Assert.Contains(services, d => d.ServiceType == typeof(IMediator));
    }

    [Fact]
    public void AddNotificationsApplication_RegistersPipelineBehaviors()
    {
        var services = new ServiceCollection();

        services.AddNotificationsApplication();

        Assert.Contains(
            services,
            d => d.ServiceType == typeof(IPipelineBehavior<,>) && d.ImplementationType == typeof(LoggingBehavior<,>));
        Assert.Contains(
            services,
            d => d.ServiceType == typeof(IPipelineBehavior<,>) && d.ImplementationType == typeof(ValidationBehavior<,>));
    }
}
