using BuildingBlocks.Application.Behaviors;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Orders.Application.DependencyInjection;

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

    [Fact]
    public void AddOrdersApplication_RegistersMediatR()
    {
        var services = new ServiceCollection();

        services.AddOrdersApplication();

        Assert.Contains(services, d => d.ServiceType == typeof(ISender));
        Assert.Contains(services, d => d.ServiceType == typeof(IMediator));
    }

    [Fact]
    public void AddOrdersApplication_RegistersPipelineBehaviors()
    {
        var services = new ServiceCollection();

        services.AddOrdersApplication();

        var behaviors = services
            .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(d => d.ImplementationType)
            .ToList();

        Assert.Contains(typeof(LoggingBehavior<,>), behaviors);
        Assert.Contains(typeof(ValidationBehavior<,>), behaviors);
    }
}
