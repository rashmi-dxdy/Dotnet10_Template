using BuildingBlocks.Application.Behaviors;
using BuildingBlocks.Application.Cqrs;
using FluentValidation;

namespace BuildingBlocks.UnitTests.Application.Behaviors;

public sealed class ValidationBehaviorTests
{
    [Fact]
    public async Task Handle_WithNoValidators_CallsNext()
    {
        var behavior = new ValidationBehavior<TestCommand, int>([]);

        var result = await behavior.Handle(new TestCommand(string.Empty, 0), _ => Task.FromResult(42), TestContext.Current.CancellationToken);

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task Handle_WithValidRequest_CallsNext()
    {
        var behavior = new ValidationBehavior<TestCommand, int>([new TestCommandValidator()]);

        var result = await behavior.Handle(new TestCommand("name", 1), _ => Task.FromResult(42), TestContext.Current.CancellationToken);

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task Handle_WithInvalidRequest_ThrowsWithAllFailures_AndDoesNotCallNext()
    {
        var behavior = new ValidationBehavior<TestCommand, int>([new TestCommandValidator()]);
        var nextCalled = false;

        var exception = await Assert.ThrowsAsync<ValidationException>(() => behavior.Handle(
            new TestCommand(string.Empty, 0),
            _ =>
            {
                nextCalled = true;
                return Task.FromResult(42);
            },
            TestContext.Current.CancellationToken));

        Assert.False(nextCalled);
        Assert.Equal<string>(
            [nameof(TestCommand.Name), nameof(TestCommand.Quantity)],
            exception.Errors.Select(e => e.PropertyName).Order(StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task Handle_CombinesFailuresFromAllValidators()
    {
        var behavior = new ValidationBehavior<TestCommand, int>([new TestCommandValidator(), new TestCommandValidator()]);

        var exception = await Assert.ThrowsAsync<ValidationException>(() => behavior.Handle(
            new TestCommand(string.Empty, 1),
            _ => Task.FromResult(42),
            TestContext.Current.CancellationToken));

        Assert.Equal(2, exception.Errors.Count());
    }

    public sealed record TestCommand(string Name, int Quantity) : ICommand<int>;

    private sealed class TestCommandValidator : AbstractValidator<TestCommand>
    {
        public TestCommandValidator()
        {
            RuleFor(c => c.Name).NotEmpty();
            RuleFor(c => c.Quantity).GreaterThan(0);
        }
    }
}
