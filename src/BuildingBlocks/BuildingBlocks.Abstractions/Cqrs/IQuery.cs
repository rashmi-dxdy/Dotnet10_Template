using System.Diagnostics.CodeAnalysis;

namespace BuildingBlocks.Abstractions.Cqrs;

/// <summary>
/// A request that reads state without changing it and returns <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="TResult">The type of the data returned by the query.</typeparam>
[SuppressMessage(
    "Major Code Smell",
    "S2326:Unused type parameters should be removed",
    Justification = "Marker type parameter: binds a query to its result type so IQueryHandler is type-checked.")]
public interface IQuery<out TResult>
{
}
