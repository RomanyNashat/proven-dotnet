using Dapper;
using SkillSamples.Postgres;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.Upgrade;

// Npgsql 10 maps PostgreSQL date to DateOnly and time to TimeOnly where nothing names the type.
public sealed class NpgsqlDateTests(PgDatabase db) : IClassFixture<PgDatabase>, IAsyncLifetime
{
    public sealed record VisitDay(int Id, DateOnly Day, TimeOnly StartsAt);

    public Task InitializeAsync() => db.ExecuteAsync("""
        CREATE TABLE visit (id int PRIMARY KEY, day date NOT NULL, starts_at time NOT NULL);
        INSERT INTO visit VALUES (1, '2026-11-14', '09:30');
        """);

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task UntypedRead_OnNpgsql10_ReturnsDateOnlyAndTimeOnly()
    {
        // Given a service on Npgsql 10 that reads a date and a time without naming a type
        await using var connection = await db.DataSource.OpenConnectionAsync();

        // When it reads them, then they come back as DateOnly and TimeOnly, not DateTime and TimeSpan
        Assert.IsType<DateOnly>(await connection.ExecuteScalarAsync<object>("SELECT day FROM visit WHERE id = 1"));
        Assert.IsType<TimeOnly>(await connection.ExecuteScalarAsync<object>("SELECT starts_at FROM visit WHERE id = 1"));
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task RecordWithDateOnlyAndTimeOnly_ReadsTheRow()
    {
        await using var connection = await db.DataSource.OpenConnectionAsync();

        var visit = await connection.QuerySingleAsync<VisitDay>(
            "SELECT id AS Id, day AS Day, starts_at AS StartsAt FROM visit WHERE id = 1");

        Assert.Equal(new VisitDay(1, new DateOnly(2026, 11, 14), new TimeOnly(9, 30)), visit);
    }

    [Fact]
    public async Task TypedRead_AsDateTime_StillWorks()
    {
        await using var connection = await db.DataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT day FROM visit WHERE id = 1";
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        Assert.Equal(new DateTime(2026, 11, 14), reader.GetFieldValue<DateTime>(0));
    }
}
