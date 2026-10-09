using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace SkillSamples.MsSql;

public static class Inventory
{
    // UPDLOCK + HOLDLOCK hold the key range from the UPDATE to the INSERT, so a second caller waits and
    // then updates. Without them both see no row and both insert: one fails on the primary key.
    public static Task SaveAsync(SqlConnection connection, int productId, int warehouseId, int quantity, CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition("""
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            UPDATE product_inventory WITH (UPDLOCK, HOLDLOCK)
               SET quantity = @quantity
             WHERE product_id = @productId AND warehouse_id = @warehouseId;
            IF @@ROWCOUNT = 0
                INSERT INTO product_inventory (product_id, warehouse_id, quantity) VALUES (@productId, @warehouseId, @quantity);
            COMMIT;
            """, new { productId, warehouseId, quantity }, cancellationToken: ct));

    // MERGE is one statement but not atomic on its own: it needs HOLDLOCK for the same reason.
    public static Task MergeAsync(SqlConnection connection, int productId, int warehouseId, int quantity, CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition("""
            MERGE INTO product_inventory WITH (HOLDLOCK) AS target
            USING (VALUES (@productId, @warehouseId, @quantity)) AS source (product_id, warehouse_id, quantity)
               ON target.product_id = source.product_id AND target.warehouse_id = source.warehouse_id
            WHEN MATCHED THEN UPDATE SET quantity = source.quantity
            WHEN NOT MATCHED THEN INSERT (product_id, warehouse_id, quantity) VALUES (source.product_id, source.warehouse_id, source.quantity);
            """, new { productId, warehouseId, quantity }, cancellationToken: ct));

    // Any number of ids in one parameter, typed and indexed: a table-valued parameter.
    // Needs once, in a reviewed script: CREATE TYPE dbo.int_list AS TABLE (id int NOT NULL PRIMARY KEY);
    public static async Task<IReadOnlyList<int>> InStockAsync(SqlConnection connection, IEnumerable<int> productIds, CancellationToken ct)
    {
        using var ids = new DataTable();
        ids.Columns.Add("id", typeof(int));
        foreach (var id in productIds.Distinct())
        {
            ids.Rows.Add(id);
        }

        var rows = await connection.QueryAsync<int>(new CommandDefinition("""
            SELECT DISTINCT i.product_id
              FROM product_inventory i
              JOIN @ids p ON p.id = i.product_id
             WHERE i.quantity > 0
            """, new { ids = ids.AsTableValuedParameter("dbo.int_list") }, cancellationToken: ct));
        return [.. rows];
    }
}
