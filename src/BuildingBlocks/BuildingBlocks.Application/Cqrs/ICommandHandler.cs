using MediatR;

namespace BuildingBlocks.Application.Cqrs;

/// <summary>
/// Handles a single command type. Exactly one handler exists per command.
/// Implement <see cref="IRequestHandler{TRequest, TResponse}.Handle"/>; MediatR discovers it by assembly scan.
/// </summary>
/// <typeparam name="TCommand">The command type.</typeparam>
/// <typeparam name="TResult">The result type declared by the command.</typeparam>
public interface ICommandHandler<in TCommand, TResult> : IRequestHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
}
