using BuildingBlocks.Application.Behaviors;
using BuildingBlocks.Application.Cqrs;
using BuildingBlocks.Application.DependencyInjection;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.UnitTests.Application;

/// <summary>
/// In the modular monolith every module calls <c>AddCqrs</c> for its own Application assembly, into ONE container.
/// The shared behaviors must still run once per request, not once per module.
/// </summary>
public sealed class CqrsRegistrationTests
{
    [Fact]
    public async Task AddCqrs_CalledByEveryModule_RegistersSharedBehaviorsOnce_AndEveryModulesHandlers()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // Three "modules": this assembly, plus two others standing in for other Application assemblies.
        services.AddCqrs(typeof(CqrsRegistrationTests).Assembly);
        services.AddCqrs(typeof(LoggingBehavior<,>).Assembly);
        services.AddCqrs(typeof(Xunit.FactAttribute).Assembly);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var behaviors = scope.ServiceProvider
            .GetServices<IPipelineBehavior<PingQuery, string>>()
            .Select(b => b.GetType().GetGenericTypeDefinition())
            .ToList();

        Assert.Equal([typeof(LoggingBehavior<,>), typeof(ValidationBehavior<,>)], behaviors);
        Assert.Single(scope.ServiceProvider.GetServices<IRequestHandler<PingQuery, string>>());
        Assert.Equal(
            "pong",
            await scope.ServiceProvider.GetRequiredService<ISender>()
                .Send(new PingQuery(), TestContext.Current.CancellationToken));
    }

    public sealed record PingQuery : IQuery<string>;

    /// <summary>Found by MediatR assembly scanning.</summary>
    public sealed class PingQueryHandler : IQueryHandler<PingQuery, string>
    {
        public Task<string> Handle(PingQuery request, CancellationToken cancellationToken) => Task.FromResult("pong");
    }
}
