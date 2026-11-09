using FluentValidation;
using FluentValidation.Results;
using MediatR;

namespace BuildingBlocks.Application.Behaviors;

/// <summary>
/// Runs every FluentValidation <see cref="IValidator{T}"/> registered for <typeparamref name="TRequest"/>
/// before the handler. If any rule fails, the handler is not called and a
/// <see cref="ValidationException"/> carrying all failures is thrown.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <param name="validators">The validators registered for the request; empty when there are none.</param>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IValidator<TRequest>[] _validators = [.. validators];

    /// <inheritdoc />
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (_validators.Length == 0)
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }

        // One context per validator: FluentValidation accumulates failures on the context, so a shared
        // context would make every result contain the failures of the validators that ran before it.
        var results = await Task.WhenAll(
                _validators.Select(v => v.ValidateAsync(new ValidationContext<TRequest>(request), cancellationToken)))
            .ConfigureAwait(false);

        List<ValidationFailure> failures = [.. results.SelectMany(r => r.Errors)];
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        return await next(cancellationToken).ConfigureAwait(false);
    }
}
