using Dapper;
using SkillSamples.DapperReads;
using Xunit;

namespace SkillSamples.Workers;

/// <summary>The same check on both engines.</summary>
public abstract class WorkClaimTests<TEngine>(TEngine engine) where TEngine : EngineFixture
{
    protected TEngine Db => engine;

    [Fact]
    public async Task TwoPodsClaimAtOnce_GetDifferentRows_WithoutWaiting()
    {
        var sqlServer = engine.Engine == Engine.SqlServer;
        await using (var setup = await engine.OpenAsync(CancellationToken.None))
        {
            await setup.ExecuteAsync(sqlServer
                ? "CREATE TABLE work_items (id int IDENTITY PRIMARY KEY, status varchar(20) NOT NULL, claimed_by varchar(50) NULL)"
                : "CREATE TABLE work_items (id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY, status varchar(20) NOT NULL, claimed_by varchar(50) NULL)");
            for (var i = 0; i < 20; i++)
            {
                await setup.ExecuteAsync("INSERT INTO work_items (status) VALUES ('Pending')");
            }
        }

        var claim = sqlServer ? WorkClaims.SqlServer : WorkClaims.PostgreSql;
        await using var podA = await engine.OpenAsync(CancellationToken.None);
        await using var podB = await engine.OpenAsync(CancellationToken.None);
        await using var txA = await podA.BeginTransactionAsync();
        await using var txB = await podB.BeginTransactionAsync();

        // A still holds its rows (open transaction) when B claims: B must skip them, not block.
        var claimedByA = (await podA.QueryAsync<int>(new CommandDefinition(claim, new { pod = "a", n = 10 }, txA))).ToList();
        var claimedByB = (await podB.QueryAsync<int>(new CommandDefinition(claim, new { pod = "b", n = 10 }, txB, commandTimeout: 5))).ToList();
        await txA.CommitAsync();
        await txB.CommitAsync();

        Assert.Equal(10, claimedByA.Count);
        Assert.Equal(10, claimedByB.Count);
        Assert.Empty(claimedByA.Intersect(claimedByB));
    }
}

public sealed class PostgresWorkClaimTests(PostgresEngine db) : WorkClaimTests<PostgresEngine>(db), IClassFixture<PostgresEngine>;

public sealed class SqlServerWorkClaimTests(SqlServerEngine db) : WorkClaimTests<SqlServerEngine>(db), IClassFixture<SqlServerEngine>;
