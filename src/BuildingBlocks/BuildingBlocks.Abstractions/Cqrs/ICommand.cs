using System.Diagnostics.CodeAnalysis;

namespace BuildingBlocks.Abstractions.Cqrs;

/// <summary>
/// A request that changes state and returns <typeparamref name="TResult"/>
/// (typically an identifier or an operation result, never a read model).
/// </summary>
/// <typeparam name="TResult">The type returned when the command has been handled.</typeparam>
[SuppressMessage(
    "Major Code Smell",
    "S2326:Unused type parameters should be removed",
    Justification = "Marker type parameter: binds a command to its result type so ICommandHandler is type-checked.")]
public interface ICommand<out TResult>
{
}
