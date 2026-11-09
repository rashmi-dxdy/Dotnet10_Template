namespace BuildingBlocks.Abstractions.Cqrs;

/// <summary>
/// Handles a single command type. Exactly one handler exists per command.
/// </summary>
/// <typeparam name="TCommand">The command type.</typeparam>
/// <typeparam name="TResult">The result type declared by the command.</typeparam>
public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    /// <summary>Handles the command.</summary>
    /// <param name="command">The command to handle.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The command result.</returns>
    Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken);
}
