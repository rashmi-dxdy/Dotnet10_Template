using BuildingBlocks.Application.Behaviors;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Catalog.Application.DependencyInjection;

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

        Assert.Contains(
            services,
            d => d.ServiceType == typeof(IPipelineBehavior<,>) && d.ImplementationType == typeof(LoggingBehavior<,>));
        Assert.Contains(
            services,
            d => d.ServiceType == typeof(IPipelineBehavior<,>) && d.ImplementationType == typeof(ValidationBehavior<,>));
    }
}
