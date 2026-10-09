using Dapper;
using Npgsql;

namespace SkillSamples.Postgres;

public static class Preferences
{
    // One statement: two requests at once can't both insert; the second updates.
    public static Task SaveAsync(NpgsqlConnection connection, int userId, string key, string value, DateTimeOffset now, CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO user_preferences (user_id, preference_key, preference_value, updated_at)
            VALUES (@userId, @key, @value, @now)
            ON CONFLICT (user_id, preference_key)
            DO UPDATE SET preference_value = EXCLUDED.preference_value, updated_at = EXCLUDED.updated_at
            """, new { userId, key, value, now }, cancellationToken: ct));

    // Many rows in one statement: one array per column, unnest pairs them up by position.
    public static Task SaveStockAsync(NpgsqlConnection connection, int[] productIds, int[] warehouseIds, int[] quantities, CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO product_inventory (product_id, warehouse_id, quantity)
            SELECT * FROM unnest(@productIds, @warehouseIds, @quantities)
            ON CONFLICT (product_id, warehouse_id) DO UPDATE SET quantity = EXCLUDED.quantity
            """, new { productIds, warehouseIds, quantities }, cancellationToken: ct));
}
