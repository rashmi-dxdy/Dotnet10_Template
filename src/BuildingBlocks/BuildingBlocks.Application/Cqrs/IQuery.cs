using MediatR;

namespace BuildingBlocks.Application.Cqrs;

/// <summary>
/// A request that reads state without changing it and returns <typeparamref name="TResult"/>.
/// Send it with MediatR's <see cref="ISender"/>.
/// </summary>
/// <typeparam name="TResult">The type of the data returned by the query.</typeparam>
public interface IQuery<out TResult> : IRequest<TResult>
{
}
