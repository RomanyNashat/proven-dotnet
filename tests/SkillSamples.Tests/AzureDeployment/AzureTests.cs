using Azure.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using SkillSamples.MsSql;
using SkillSamples.Postgres;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.AzureDeployment;

public sealed class AzurePostgresTests(PgDatabase pg) : IClassFixture<PgDatabase>
{
    // Stands in for DefaultAzureCredential: hands out the next token each time it's asked.
    private sealed class Tokens(params string[] tokens) : TokenCredential
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            Next();

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Next());

        private AccessToken Next()
        {
            var call = Interlocked.Increment(ref _calls);
            return new AccessToken(tokens[Math.Min(call, tokens.Length) - 1], DateTimeOffset.UtcNow.AddHours(1));
        }
    }

    // The test server's password plays the token; the connection string itself has none.
    private string WithoutPassword => new NpgsqlConnectionStringBuilder(pg.ConnectionString) { Password = null }.ConnectionString;

    private static async Task<int> SelectOneAsync(NpgsqlDataSource dataSource)
    {
        await using var command = dataSource.CreateCommand("SELECT 1");
        return (int)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task Postgres_TheTokenIsThePassword()
    {
        var tokens = new Tokens("postgres");
        await using var dataSource = AzurePostgres.DataSource(WithoutPassword, tokens, TimeSpan.FromMinutes(55));

        Assert.Equal(1, await SelectOneAsync(dataSource));
        Assert.True(tokens.Calls >= 1);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_FirstTokenIsRefused_TheRefreshFixesItWithoutARestart()
    {
        // Given the identity's first token is refused (its database role was granted a minute late)
        var tokens = new Tokens("not-the-password", "postgres");
        await using var dataSource = AzurePostgres.DataSource(WithoutPassword, tokens, TimeSpan.FromSeconds(1));
        await Assert.ThrowsAsync<PostgresException>(() => SelectOneAsync(dataSource));

        // When the provider fetches the next token on its timer
        var result = 0;
        for (var i = 0; i < 50 && result == 0; i++)
        {
            await Task.Delay(200);
            try
            {
                result = await SelectOneAsync(dataSource);
            }
            catch (PostgresException)
            {
                // still the refused token
            }
        }

        // Then new connections succeed, with the same data source and no restart
        Assert.Equal(1, result);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public async Task Production_NoIcuNoTzdata_TheTokenPasswordStillWorks()
    {
        ProductionConditions.Require();
        await using var dataSource = AzurePostgres.DataSource(WithoutPassword, new Tokens("postgres"), TimeSpan.FromMinutes(55));

        Assert.Equal(1, await SelectOneAsync(dataSource));
    }
}

public sealed class AzureSqlTests(SqlDatabase sql) : IClassFixture<SqlDatabase>
{
    [Fact]
    public async Task AzureSql_ConnectsWithRetriesOn()
    {
        await using var context = new OrdersDbContext(AzureSql.Options(sql.ConnectionString));

        Assert.True(await context.Database.CanConnectAsync());
        Assert.True(context.Database.CreateExecutionStrategy().RetriesOnFailure);
    }

    [Fact]
    public void UseAzureSqlDefaults_IsObsolete()
    {
        var method = typeof(SqlServerDbContextOptionsBuilder).GetMethod("UseAzureSqlDefaults");

        Assert.NotNull(method);
        Assert.True(method.IsDefined(typeof(ObsoleteAttribute), inherit: false));
    }
}
