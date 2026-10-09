using System.Diagnostics;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace SkillSamples.Cqrs;

// What MediatR calls pipeline behaviours: plain decorators around the handler.
// Order for commands: logging → validation → transaction → handler.

public sealed class LoggingCommandDecorator<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner, ILogger<LoggingCommandDecorator<TCommand, TResult>> logger)
    : ICommandHandler<TCommand, TResult> where TCommand : ICommand<TResult>
{
    public async Task<TResult> HandleAsync(TCommand command, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        var result = await inner.HandleAsync(command, ct);
        var elapsed = Stopwatch.GetElapsedTime(started);
        // The name and the time only: a command carries user input, which can be patient data.
        logger.Log(elapsed.TotalMilliseconds > 500 ? LogLevel.Warning : LogLevel.Information,
            "Handled {Command} in {ElapsedMs:F0} ms", typeof(TCommand).Name, elapsed.TotalMilliseconds);
        return result;
    }
}

public sealed class ValidationDecorator<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner, IEnumerable<IValidator<TCommand>> validators)
    : ICommandHandler<TCommand, TResult> where TCommand : ICommand<TResult>
{
    public async Task<TResult> HandleAsync(TCommand command, CancellationToken ct)
    {
        var failures = new List<FluentValidation.Results.ValidationFailure>();
        foreach (var validator in validators)
        {
            failures.AddRange((await validator.ValidateAsync(command, ct)).Errors);
        }

        if (failures.Count > 0) throw new ValidationException(failures);   // → 400 ProblemDetails
        return await inner.HandleAsync(command, ct);
    }
}

public sealed class TransactionDecorator<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner, IUnitOfWork unitOfWork)
    : ICommandHandler<TCommand, TResult> where TCommand : ICommand<TResult>
{
    public Task<TResult> HandleAsync(TCommand command, CancellationToken ct) =>
        unitOfWork.ExecuteInTransactionAsync(token => inner.HandleAsync(command, token), ct);
}

public sealed class LoggingQueryDecorator<TQuery, TResult>(
    IQueryHandler<TQuery, TResult> inner, ILogger<LoggingQueryDecorator<TQuery, TResult>> logger)
    : IQueryHandler<TQuery, TResult> where TQuery : IQuery<TResult>
{
    public async Task<TResult> HandleAsync(TQuery query, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        var result = await inner.HandleAsync(query, ct);
        var elapsed = Stopwatch.GetElapsedTime(started);
        logger.Log(elapsed.TotalMilliseconds > 500 ? LogLevel.Warning : LogLevel.Information,
            "Handled {Query} in {ElapsedMs:F0} ms", typeof(TQuery).Name, elapsed.TotalMilliseconds);
        return result;
    }
}
