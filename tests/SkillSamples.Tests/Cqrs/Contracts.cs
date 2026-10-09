namespace SkillSamples.Cqrs;

// Application layer. No library: a command or query is a record, a handler is a class, and the endpoint
// asks DI for the handler it needs.
public interface ICommand<TResult>;
public interface IQuery<TResult>;

public interface ICommandHandler<in TCommand, TResult> where TCommand : ICommand<TResult>
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken ct);
}

public interface IQueryHandler<in TQuery, TResult> where TQuery : IQuery<TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken ct);
}

/// <summary>Runs work in one database transaction (EF Core: execution strategy + BeginTransaction).</summary>
public interface IUnitOfWork
{
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct);
}
