using System.Data.Common;
using System.Text.Json;
using Dapper;

namespace SkillSamples.DapperReads;

public enum Engine { PostgreSql, SqlServer }

public sealed record OrderRow(int Id, int CustomerId, decimal Total, DateTime CreatedAt);

public sealed record NewOrder(int CustomerId, decimal Total);

/// <summary>
/// Read side with Dapper, on either engine. Every call goes through a CommandDefinition so the
/// CancellationToken reaches the database; the anonymous-object overloads take no token at all.
/// </summary>
public sealed class OrderReads(Func<CancellationToken, Task<DbConnection>> open, Engine engine)
{
    private const string Columns = "id AS Id, customer_id AS CustomerId, total AS Total, created_at AS CreatedAt";

    // The only sort columns a caller can pick. User input selects a key; it never becomes SQL.
    private static readonly Dictionary<string, string> SortColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["created"] = "created_at",
        ["total"] = "total",
    };

    /// <summary>Keyset paging: the next page after the last id the caller saw.</summary>
    public async Task<IReadOnlyList<OrderRow>> PageAsync(int customerId, int afterId, int size, CancellationToken ct)
    {
        var sql = engine == Engine.PostgreSql
            ? $"SELECT {Columns} FROM orders WHERE customer_id = @customerId AND id > @afterId ORDER BY id LIMIT @size"
            : $"SELECT TOP (@size) {Columns} FROM orders WHERE customer_id = @customerId AND id > @afterId ORDER BY id";
        await using var connection = await open(ct);
        var rows = await connection.QueryAsync<OrderRow>(new CommandDefinition(sql, new { customerId, afterId, size }, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>A caller-chosen sort, through the allowlist.</summary>
    public async Task<IReadOnlyList<OrderRow>> TopAsync(int customerId, string sort, int size, CancellationToken ct)
    {
        if (!SortColumns.TryGetValue(sort, out var column))
        {
            throw new ArgumentException($"Unknown sort '{sort}'", nameof(sort));
        }

        var sql = engine == Engine.PostgreSql
            ? $"SELECT {Columns} FROM orders WHERE customer_id = @customerId ORDER BY {column} DESC, id DESC LIMIT @size"
            : $"SELECT TOP (@size) {Columns} FROM orders WHERE customer_id = @customerId ORDER BY {column} DESC, id DESC";
        await using var connection = await open(ct);
        var rows = await connection.QueryAsync<OrderRow>(new CommandDefinition(sql, new { customerId, size }, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>
    /// Any number of ids in one parameter. PostgreSQL takes an int[]; SQL Server takes JSON. Dapper's
    /// `IN @ids` expands to one parameter per id, and SQL Server refuses more than 2,100.
    /// </summary>
    public async Task<IReadOnlyList<OrderRow>> ByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken ct)
    {
        var (sql, args) = engine == Engine.PostgreSql
            ? ($"SELECT {Columns} FROM orders WHERE id = ANY(@ids)", (object)new { ids = ids.ToArray() })
            : ($"SELECT {Columns} FROM orders WHERE id IN (SELECT CAST(value AS int) FROM OPENJSON(@ids))",
               new { ids = JsonSerializer.Serialize(ids) });
        await using var connection = await open(ct);
        var rows = await connection.QueryAsync<OrderRow>(new CommandDefinition(sql, args, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>
    /// Many rows in one command. Passing a list to ExecuteAsync runs the INSERT once per row.
    /// </summary>
    public async Task<int> InsertManyAsync(IReadOnlyCollection<NewOrder> orders, CancellationToken ct)
    {
        var (sql, args) = engine == Engine.PostgreSql
            ? ("INSERT INTO orders (customer_id, total) SELECT * FROM unnest(@customerIds, @totals)",
               (object)new { customerIds = orders.Select(o => o.CustomerId).ToArray(), totals = orders.Select(o => o.Total).ToArray() })
            : ("""
               INSERT INTO orders (customer_id, total)
               SELECT customer_id, total FROM OPENJSON(@rows) WITH (customer_id int '$.CustomerId', total decimal(18,2) '$.Total')
               """,
               new { rows = JsonSerializer.Serialize(orders) });
        await using var connection = await open(ct);
        return await connection.ExecuteAsync(new CommandDefinition(sql, args, cancellationToken: ct));
    }
}
