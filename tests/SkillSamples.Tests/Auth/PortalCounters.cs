using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace SkillSamples.Auth;

/// <summary>What the auth server did: introspection requests it received, and SQL commands its store ran.</summary>
public sealed class PortalCounters : DbCommandInterceptor
{
    private int _introspections;
    private int _commands;

    public int Introspections => Volatile.Read(ref _introspections);

    public int Commands => Volatile.Read(ref _commands);

    public void Reset()
    {
        Interlocked.Exchange(ref _introspections, 0);
        Interlocked.Exchange(ref _commands, 0);
    }

    public void CountIntrospection() => Interlocked.Increment(ref _introspections);

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _commands);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _commands);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _commands);
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }
}
