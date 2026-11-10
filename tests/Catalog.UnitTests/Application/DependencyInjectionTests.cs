using BuildingBlocks.Application.Behaviors;
using Catalog.Application.DependencyInjection;
using MediatR;
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

    [Fact]
    public void AddCatalogApplication_RegistersMediatR()
    {
        var services = new ServiceCollection();

        services.AddCatalogApplication();

        Assert.Contains(services, d => d.ServiceType == typeof(ISender));
        Assert.Contains(services, d => d.ServiceType == typeof(IMediator));
    }

    [Fact]
    public void AddCatalogApplication_RegistersPipelineBehaviors()
    {
        var services = new ServiceCollection();

        services.AddCatalogApplication();

        var behaviors = services
            .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(d => d.ImplementationType)
            .ToList();

        Assert.Contains(typeof(LoggingBehavior<,>), behaviors);
        Assert.Contains(typeof(ValidationBehavior<,>), behaviors);
    }
}
