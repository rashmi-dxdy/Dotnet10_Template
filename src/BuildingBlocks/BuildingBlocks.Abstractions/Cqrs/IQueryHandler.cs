namespace BuildingBlocks.Abstractions.Cqrs;

/// <summary>
/// Handles a single query type. Exactly one handler exists per query.
/// </summary>
/// <typeparam name="TQuery">The query type.</typeparam>
/// <typeparam name="TResult">The result type declared by the query.</typeparam>
public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    /// <summary>Handles the query.</summary>
    /// <param name="query">The query to handle.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The query result.</returns>
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken);
}
