using MediatR;

namespace BuildingBlocks.Application.Cqrs;

/// <summary>
/// Handles a single query type. Exactly one handler exists per query.
/// Implement <see cref="IRequestHandler{TRequest, TResponse}.Handle"/>; MediatR discovers it by assembly scan.
/// </summary>
/// <typeparam name="TQuery">The query type.</typeparam>
/// <typeparam name="TResult">The result type declared by the query.</typeparam>
public interface IQueryHandler<in TQuery, TResult> : IRequestHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>
{
}
