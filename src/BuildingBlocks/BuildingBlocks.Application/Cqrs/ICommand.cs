using MediatR;

namespace BuildingBlocks.Application.Cqrs;

/// <summary>
/// A request that changes state and returns <typeparamref name="TResult"/>
/// (typically an identifier or an operation result, never a read model).
/// Send it with MediatR's <see cref="ISender"/>.
/// </summary>
/// <typeparam name="TResult">The type returned when the command has been handled.</typeparam>
public interface ICommand<out TResult> : IRequest<TResult>
{
}
