using System.Transactions;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.MsSql;

// EF Core 10 adds its own Application Name to a SQL Server connection string that has none. Code that
// writes with EF and with Dapper in one TransactionScope then opens two connections with different
// strings, and the transaction is promoted to a distributed one, which .NET doesn't run on Linux.
public sealed class SharedTransactionTests(SqlDatabase database) : IClassFixture<SqlDatabase>
{
    private sealed class Visit
    {
        public int Id { get; set; }
        public required string Note { get; set; }
    }

    private sealed class VisitsContext(DbContextOptions<VisitsContext> options) : DbContext(options)
    {
        public DbSet<Visit> Visits => Set<Visit>();

        protected override void OnModelCreating(ModelBuilder model) =>
            model.Entity<Visit>(visit =>
            {
                visit.ToTable("Visit");
                visit.Property(v => v.Note).HasMaxLength(100);
            });
    }

    private async Task CreateTablesAsync() => await database.ExecuteAsync("""
        IF OBJECT_ID('Visit') IS NULL CREATE TABLE Visit (Id int IDENTITY PRIMARY KEY, Note nvarchar(100) NOT NULL);
        IF OBJECT_ID('VisitAudit') IS NULL CREATE TABLE VisitAudit (Id int IDENTITY PRIMARY KEY, VisitId int NOT NULL);
        """);

    // The service's write: the visit through EF, its audit row through Dapper, both or neither.
    private static async Task BookAsync(string setting, string note)
    {
        using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
        await using (var context = new VisitsContext(new DbContextOptionsBuilder<VisitsContext>().UseSqlServer(setting).Options))
        {
            var visit = new Visit { Note = note };
            context.Visits.Add(visit);
            await context.SaveChangesAsync();
            await using var connection = new SqlConnection(setting);
            await connection.ExecuteAsync("INSERT INTO VisitAudit (VisitId) VALUES (@Id)", new { visit.Id });
        }
        scope.Complete();
    }

    [Fact]
    public async Task Trap_NoApplicationName_EfAndDapperInOneScope_NeedADistributedTransaction()
    {
        await CreateTablesAsync();

        var booking = await Record.ExceptionAsync(() => BookAsync(database.ConnectionString, "no name"));

        Assert.NotNull(booking);
        Assert.Contains("distributed", booking.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT COUNT(*) FROM Visit WHERE Note = 'no name'"));
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_TheSettingNamesTheService_SoEfAndDapperShareOneConnection()
    {
        // Given: the connection string setting names the service, as the upgrade skill says
        await CreateTablesAsync();
        var setting = new SqlConnectionStringBuilder(database.ConnectionString) { ApplicationName = "visits-api" }.ConnectionString;

        // When: a visit is booked with its audit row in one scope, on EF Core 10
        await BookAsync(setting, "named");

        // Then: both rows are there, in one local transaction
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT COUNT(*) FROM Visit WHERE Note = 'named'"));
        Assert.Equal(1, await database.ScalarAsync<int>(
            "SELECT COUNT(*) FROM VisitAudit a JOIN Visit v ON v.Id = a.VisitId WHERE v.Note = 'named'"));
    }
}
