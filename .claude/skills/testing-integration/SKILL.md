---
name: testing-integration
description: Integration testing for .NET — WebApplicationFactory against a real PostgreSQL or SQL Server in Testcontainers, schema from SQL (never Migrate), Respawn between tests, a test auth handler, ownership checks. Factory and tests run in CI on both engines.
version: 2.0.0
---

# Integration Testing

The model knows WebApplicationFactory and Testcontainers. This skill holds what's specific here, and the
places where the common example breaks these rules. The code marked as a sample is tested in CI against
**both** PostgreSQL 17 and SQL Server 2022: a small service (`tests/SkillSamples.OrdersApi`) started by
its tests (`tests/SkillSamples.Tests/Integration`), the same tests on each engine.

Related: `testing-tdd` (unit tests, naming), `efcore-patterns` (the context under test), `api-design`
(endpoints, ProblemDetails), `rules/testing.md` (no EF InMemory, Testcontainers, Respawn).

## 1. The factory

<!-- sample: tests/SkillSamples.Tests/Integration/OrdersApiFactory.cs -->
```csharp
/// <summary>
/// Starts the real service against a real database in a container. A service has one engine and keeps
/// one subclass; both are here so CI checks both.
/// </summary>
public abstract class OrdersApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private Respawner _respawner = null!;

    protected abstract DockerContainer Container { get; }

    protected abstract string Engine { get; }

    protected abstract string ConnectionString { get; }

    protected abstract DbConnection NewConnection();

    protected abstract RespawnerOptions ResetOptions { get; }

    public async Task InitializeAsync()
    {
        await Container.StartAsync();

        // Build the schema from SQL, never Migrate() (rules/efcore-rules.md). This generates it from the
        // model; a service with committed migration scripts runs those files in order instead, which
        // tests the scripts the pipeline will run.
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            foreach (var batch in Regex.Split(db.Database.GenerateCreateScript(), @"^\s*GO\s*$", RegexOptions.Multiline))
            {
                if (!string.IsNullOrWhiteSpace(batch))
                {
                    await db.Database.ExecuteSqlRawAsync(batch);
                }
            }
        }

        await using var connection = NewConnection();
        await connection.OpenAsync();
        _respawner = await Respawner.CreateAsync(connection, ResetOptions);
    }

    /// <summary>Empties the tables between tests: one container per class, clean data per test.</summary>
    public async Task ResetDatabaseAsync()
    {
        await using var connection = NewConnection();
        await connection.OpenAsync();
        await _respawner.ResetAsync(connection);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await Container.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Configuration, not service surgery: the app reads these when it builds the DbContext. Removing
        // DbContextOptions<T> and calling AddDbContext again leaves the app's provider registered on EF 9+.
        builder.UseSetting("ConnectionStrings:Orders", ConnectionString);
        builder.UseSetting("Database:Engine", Engine);
        builder.ConfigureTestServices(services =>
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, null));
    }
}

public sealed class PostgresOrdersApi : OrdersApiFactory
{
    // The major version production runs (rules: pin the image, never "latest").
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    protected override DockerContainer Container => _postgres;

    protected override string Engine => "PostgreSql";

    protected override string ConnectionString => _postgres.GetConnectionString();

    protected override DbConnection NewConnection() => new NpgsqlConnection(ConnectionString);

    protected override RespawnerOptions ResetOptions => new()
    {
        DbAdapter = DbAdapter.Postgres,
        SchemasToInclude = ["public"],
    };
}

public sealed class SqlServerOrdersApi : OrdersApiFactory
{
    // 2022 is the major version; Developer edition is free for development and testing.
    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    protected override DockerContainer Container => _sqlServer;

    protected override string Engine => "SqlServer";

    protected override string ConnectionString => _sqlServer.GetConnectionString();

    protected override DbConnection NewConnection() => new SqlConnection(ConnectionString);

    protected override RespawnerOptions ResetOptions => new()
    {
        DbAdapter = DbAdapter.SqlServer,
        SchemasToInclude = ["dbo"],
    };
}
```

A service has one engine and keeps the subclass for it. What the sample settles:
- **Never `Migrate()`, in tests too** (`rules/efcore-rules.md`): the schema comes from SQL. Generating it
  from the model works; running the service's committed migration scripts in order is better, because
  the tests then prove the scripts the pipeline will run.
- **Configuration, not service surgery.** The common example removes `DbContextOptions<T>` and calls
  `AddDbContext` again with the test database. From EF 9 the app's own provider registration stays behind,
  and the context ends up with two providers. Override the connection string with `UseSetting`, and let
  the app read it when the context is built:

<!-- sample: tests/SkillSamples.OrdersApi/Orders.cs -->
```csharp
public static class OrdersDb
{
    /// <summary>
    /// Reads the connection string when the context is built, not at startup, so a test can supply it
    /// with UseSetting and replace nothing. A service has one engine; this one runs on both for CI.
    /// </summary>
    public static IServiceCollection AddOrdersDb(this IServiceCollection services) =>
        services.AddDbContext<OrdersDbContext>((sp, options) =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var connectionString = config.GetConnectionString("Orders")
                ?? throw new InvalidOperationException("ConnectionStrings:Orders is not set.");
```
- **Respawn per engine:** `DbAdapter.Postgres` with schema `public`, or `DbAdapter.SqlServer` with `dbo`,
  each given an open connection of that engine's type. If your scripts create `__EFMigrationsHistory`,
  add it to `TablesToIgnore`.
- **Images:** the module builders take the image in the constructor. Pin the major version production
  runs (`postgres:17`, `mssql/server:2022-…`), never `latest`. SQL Server in a container is the Developer
  edition, free for development and testing.
- Redis, Kafka, MongoDB: the same idea. Start the container, pass its address through `UseSetting`, and
  keep the app's own registration (§5).

## 2. Who is calling

<!-- sample: tests/SkillSamples.Tests/Integration/TestAuthHandler.cs -->
```csharp
/// <summary>The test says who the caller is with a header; no header, no user (401).</summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string UserHeader = "X-Test-User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var userId) || !int.TryParse(userId, out _))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId!)], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
```

Registered in `ConfigureTestServices` as the default scheme (§1), so the app's JWT setup stays and is
simply not used. User ids are `int`, like everywhere else.

## 3. The tests

One container per test class, empty tables per test:

<!-- sample: tests/SkillSamples.Tests/Integration/OrdersApiTests.cs -->
```csharp
    [Fact]
    public async Task GetOrder_SomeoneElsesOrder_ReturnsNotFound()
    {
        using var owner = ClientFor(7);
        using var created = await owner.PostAsJsonAsync("/orders", new CreateOrder(3, 1));
        var order = await created.Content.ReadFromJsonAsync<OrderDto>();

        using var other = ClientFor(8);
        using var response = await other.GetAsync($"/orders/{order!.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/orders/{order.Id}")).StatusCode);
    }
```

Tested on both engines: create returns 201 with its `Location`; a bad quantity returns a
`ValidationProblem` naming the field; someone else's order is 404 (not 403, which tells the caller the id
exists; `rules/security.md`); no user is 401; and each test starts with empty tables.
- **Every endpoint that takes an id gets the other-user test.** Ids are sequential `int`s; this test is
  what catches a missing ownership check.
- The test class takes the factory through `IClassFixture<T>` and resets in `InitializeAsync`
  (`IAsyncLifetime`). For a large suite, one factory for many classes through a collection fixture.

## 4. Test data

- **Create data the way the app does**: through the API, or through the domain and the `DbContext`.
  Never set ids: an `int` identity is `GENERATED ALWAYS` on PostgreSQL and rejects a supplied id, and
  identity on SQL Server needs `IDENTITY_INSERT`. Read the id back from the response or the entity.
- **Raw SQL inserts are engine-specific** (`true` / `now()` on PostgreSQL, `1` / `SYSUTCDATETIME()` on SQL
  Server) and skip the domain's rules. Use them only for a state the API can't produce, and write the
  statement for your engine.
- Builders give readable defaults (`OrderBuilder.Pending().ForPatient(7)`), not ids.

## 5. Other containers

```csharp
private readonly RedisContainer _redis = new RedisBuilder("redis:7").Build();

private readonly MongoDbContainer _mongo = new MongoDbBuilder("mongo:7")
    .WithReplicaSet()          // transactions and change streams need a replica set
    .Build();

private readonly KafkaContainer _kafka = new KafkaBuilder("confluentinc/cp-kafka:7.6.0").Build();

// In ConfigureWebHost, beside the database:
builder.UseSetting("ConnectionStrings:Redis", _redis.GetConnectionString());
builder.UseSetting("Kafka:BootstrapServers", _kafka.GetBootstrapAddress());
```

External HTTP services the test doesn't own are replaced in `ConfigureTestServices` with a fake typed
client; databases and brokers are never faked (`rules/testing.md`).

## 6. Testcontainers: lifecycle, readiness, CI

- **Typed modules** (`Testcontainers.PostgreSql`, `.MsSql`, `.Redis`, `.Kafka`, `.MongoDb`) over the
  generic container: they carry the right wait strategy and `GetConnectionString()`.
- **Share the container, isolate the data.** Starting one costs seconds; start it once per class (or per
  collection) and reset tables between tests.
- **Readiness:** a started container isn't a ready database. The typed modules wait correctly; a generic
  container needs `.WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(…))`. Never
  `Task.Delay`.
- **Reuse** (`.WithReuse(true)`) only on a dev machine, behind an environment flag; never in CI.
- **CI** needs a Docker daemon (a mounted socket or Docker-in-Docker) and the Ryuk reaper enabled.

## 7. Review checklist
- `Migrate()`, `MigrateAsync()` or `EnsureCreated()` in a test fixture.
- `RemoveAll<DbContextOptions<T>>()` followed by `AddDbContext` (two providers on EF 9+).
- EF InMemory or SQLite standing in for the real engine.
- An endpoint that takes an id with no other-user test.
- Test data with hand-set ids; raw SQL inserts written for the other engine.
- `latest` image tags; a container started per test; `Task.Delay` waiting for a container.
