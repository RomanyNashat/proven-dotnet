using System.Data;
using System.Data.Common;
using MeterSync.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MeterSync.Tests.Infrastructure;

/// <summary>
/// A <see cref="SyncDbContext"/> on Npgsql that never opens a connection: there is no database here, so
/// commands are captured and answered by the interceptor.
/// </summary>
internal static class TestDb
{
    public const string DummyConnectionString = "Host=127.0.0.1;Port=1;Database=none;Username=none";

    public static SyncDbContext Create(params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<SyncDbContext>()
            .UseNpgsql(DummyConnectionString)
            .AddInterceptors(interceptors)
            .Options);
}

/// <summary>
/// Records every command. Non-queries return <c>nonQuery(command)</c>; queries return the table from
/// <c>reader(command)</c> (an empty result when none is given).
/// </summary>
internal sealed class CapturingInterceptor(Func<CapturedCommand, int> nonQuery, Func<CapturedCommand, DataTable>? reader = null)
    : IDbConnectionInterceptor, IDbCommandInterceptor
{
    public CapturingInterceptor(int rowsAffected)
        : this(_ => rowsAffected)
    {
    }

    /// <summary>Snapshots: the provider resets a command's text and parameters once it has run.</summary>
    public List<CapturedCommand> Commands { get; } = [];

    public InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result) =>
        InterceptionResult.Suppress();

    public ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(InterceptionResult.Suppress());

    public InterceptionResult ConnectionClosing(DbConnection connection, ConnectionEventData eventData, InterceptionResult result) =>
        InterceptionResult.Suppress();

    public ValueTask<InterceptionResult> ConnectionClosingAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result) =>
        ValueTask.FromResult(InterceptionResult.Suppress());

    public ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var captured = Capture(command);
        return ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(nonQuery(captured)));
    }

    public ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        var captured = Capture(command);
        var table = reader?.Invoke(captured) ?? new DataTable();
        return ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(table.CreateDataReader()));
    }

    private CapturedCommand Capture(DbCommand command)
    {
        var captured = new CapturedCommand(command.CommandText, [.. command.Parameters.Cast<DbParameter>().Select(p => p.Value)]);
        Commands.Add(captured);
        return captured;
    }
}

internal sealed record CapturedCommand(string CommandText, IReadOnlyList<object?> ParameterValues);

internal static class Table
{
    /// <summary>A one-column result, e.g. the ids a query selects.</summary>
    public static DataTable Of<T>(params T[] values)
    {
        var table = new DataTable();
        table.Columns.Add("c0", typeof(T));
        foreach (var value in values)
        {
            table.Rows.Add(value);
        }

        return table;
    }

    /// <summary>One row with the given columns, in the order the query selects them.</summary>
    public static DataTable Row(params object?[] values)
    {
        var table = new DataTable();
        for (var i = 0; i < values.Length; i++)
        {
            table.Columns.Add($"c{i}", values[i]?.GetType() ?? typeof(string));
        }

        table.Rows.Add(values.Select(v => v ?? DBNull.Value).ToArray());
        return table;
    }
}
