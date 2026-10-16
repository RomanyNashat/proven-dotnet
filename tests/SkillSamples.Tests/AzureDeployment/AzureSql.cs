using Microsoft.EntityFrameworkCore;

namespace SkillSamples.AzureDeployment;

public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options);

public static class AzureSql
{
    // Azure SQL with a managed identity: SqlClient gets the token itself, so the connection string has no
    // password ("Authentication=Active Directory Default"). UseAzureSql, not UseAzureSqlDefaults (obsolete).
    public static DbContextOptions<OrdersDbContext> Options(string connectionString) =>
        new DbContextOptionsBuilder<OrdersDbContext>()
            .UseAzureSql(connectionString, sql => sql.EnableRetryOnFailure(maxRetryCount: 5))
            .Options;
}
