---
name: aspire-patterns
description: .NET Aspire: AppHost composition, service discovery, resource wiring, ServiceDefaults telemetry.
version: 2.0.0
---

# .NET Aspire Patterns

## AppHost (Orchestration)

<!-- sample: tests/SkillSamples.Aspire/AppHost/Program.cs -->
```csharp
// AppHost/Program.cs — defines the distributed application
var builder = DistributedApplication.CreateBuilder(args);

// Infrastructure: containers locally, with data that survives a restart
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("postgres-data");
var orderDb = postgres.AddDatabase("orderdb");

// SQL Server instead (Aspire.Hosting.SqlServer):
// var sql = builder.AddSqlServer("sql").WithDataVolume("sql-data");
// var orderDb = sql.AddDatabase("orderdb");

var redis = builder.AddRedis("redis")
    .WithDataVolume("redis-data");

// Services get their connection strings from WithReference, and start once WaitFor's resources are up
builder.AddProject<Projects.OrderService_Api>("order-service")
    .WithReference(orderDb)
    .WithReference(redis)
    .WaitFor(orderDb)
    .WaitFor(redis)
    .WithExternalHttpEndpoints();

builder.Build().Run();
```

The AppHost project is `<Project Sdk="Aspire.AppHost.Sdk/13.0.0">` on .NET 10, with one hosting package per
resource (`Aspire.Hosting.PostgreSQL`, `Aspire.Hosting.Redis`); Aspire 13 needs no `Aspire.Hosting.AppHost`
reference. Tested in CI: this AppHost starts the service and the containers, and the integration test
below goes through the Redis it wired.
Aspire 13.0.0 brings MessagePack 2.5.192, which has known advisories (GHSA-qhrr-8q5h-9q3h and three more):
with NuGetAudit as errors the AppHost doesn't restore. Pin `MessagePack` 2.5.303 directly (`zero-vulns`)
until Aspire depends on a fixed version. With Central Package Management and transitive pinning,
`StackExchange.Redis` must be at least 2.9.32, the floor `Aspire.Hosting.Redis` 13 asks for, or restore
fails with NU1109. Dev tools (`WithPgAdmin()`, `WithRedisCommander()`, Kafka and
MongoDB with `AddKafka`, `AddMongoDB`) attach the same way.

## ServiceDefaults Extension

<!-- sample: tests/SkillSamples.Tests/Aspire/ServiceDefaults.cs -->
```csharp
// ServiceDefaults/Extensions.cs: shared by every service.
public static class ServiceDefaults
{
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // The template's AddStandardResilienceHandler() retries every method: a POST that timed out
            // but went through (a payment, a booking) is sent again. Only safe methods are retried here.
            http.AddStandardResilienceHandler(o => o.Retry.DisableForUnsafeHttpMethods());
            http.AddServiceDiscovery();
        });
        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation())
            .WithTracing(tracing => tracing
                // Kubernetes probes every pod every few seconds: keep them out of the traces.
                .AddAspNetCoreInstrumentation(o => o.Filter = http => !http.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation()
                .AddSource("Npgsql"));   // SQL Server: .AddSqlClientInstrumentation()

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // In every environment: Kubernetes probes Production. (The template maps these in Development only.)
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = check => check.Tags.Contains("live") });
        app.MapHealthChecks("/health/ready");
        return app;
    }
}
```

Three changes from the template, each tested (`Aspire/AspireTests.cs`):
- **POSTs aren't retried.** The template adds `AddStandardResilienceHandler()` to every client, and it
  retries every method. Tested: against a payments API answering 503, the template's defaults sent one
  payment four times; these send it once. The full story is in `polly-resilience`.
- **The health endpoints exist in Production.** The template maps them in Development only, and
  Kubernetes probes Production. Tested with the environment set to Production.
- **Probes stay out of the traces.** Every pod is probed every few seconds; tested: `/api/ping` is
  traced, `/health/live` isn't.

### Use in each service
<!-- sample: tests/SkillSamples.Aspire/OrderService.Api/Program.cs -->
```csharp
// OrderService.Api/Program.cs: the connection string for "redis" comes from the AppHost's WithReference.
var builder = WebApplication.CreateBuilder(args);
builder.AddRedisClient("redis");

var app = builder.Build();
app.MapGet("/health/live", () => "Healthy");
app.MapPost("/api/orders", async (OrderRequest order, IConnectionMultiplexer redis) =>
{
    var db = redis.GetDatabase();
    var id = await db.StringIncrementAsync("orders:next-id");
    await db.StringSetAsync($"orders:{id}", order.ProductId);
    return Results.Created($"/api/orders/{id}", new { id });
});
app.MapGet("/api/orders/{id:long}", async (long id, IConnectionMultiplexer redis) =>
{
    var productId = await redis.GetDatabase().StringGetAsync($"orders:{id}");
    return productId.IsNull ? Results.NotFound() : Results.Ok(new { id, productId = (int)productId });
});
app.Run();

internal sealed record OrderRequest(int ProductId, int Quantity);
```

The other client integrations follow the same shape: `builder.AddNpgsqlDbContext<AppDbContext>("orderdb")`
(SQL Server: `AddSqlServerDbContext`), `builder.AddKafkaProducer<string, string>("kafka")`, each named after
the AppHost resource. Add `builder.AddServiceDefaults()` and `app.MapDefaultEndpoints()` (above) to each.

## Service Discovery

```csharp
// Services reference each other by name, not URLs
// Aspire resolves "order-service" → actual host:port at runtime

builder.Services.AddHttpClient<IOrderServiceClient>(client =>
{
    client.BaseAddress = new Uri("https+http://order-service");
});

// gRPC service discovery
builder.Services.AddGrpcClient<OrderGrpcService.OrderGrpcServiceClient>(options =>
{
    options.Address = new Uri("https+http://order-service");
});

// "https+http://": HTTPS when the service has an HTTPS endpoint, HTTP otherwise,
// with the name resolved by service discovery (configuration, or Kubernetes DNS)
```

Tested with endpoints from configuration (`services:orders:http:0`, `services:orders:https:0`): the
request goes to the HTTPS endpoint when there is one and to the HTTP one when there isn't.

## Aspire Dashboard

The dashboard is automatically available at `https://localhost:18888` when running the AppHost. It provides:

- **Traces**: Distributed traces across all services (OpenTelemetry)
- **Logs**: Structured logs from all services (Serilog/OpenTelemetry)
- **Metrics**: Runtime metrics, HTTP metrics, custom metrics
- **Resources**: Service status, endpoints, environment variables

No configuration needed — the AppHost injects OTLP exporter endpoints into all services automatically.

## Integration Testing with Aspire

<!-- sample: tests/SkillSamples.Aspire/Tests/OrderApiTests.cs -->
```csharp
public sealed class OrderApiTests : IAsyncLifetime
{
    private DistributedApplication _app = null!;
    private HttpClient _http = null!;

    private sealed record Order(long Id, int ProductId);

    public async Task InitializeAsync()
    {
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>();
        _app = await appHost.BuildAsync();
        await _app.StartAsync();

        // WaitFor in the AppHost holds the service until Postgres and Redis are up; wait for the service here.
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("order-service").WaitAsync(TimeSpan.FromMinutes(5));
        _http = _app.CreateHttpClient("order-service");
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task CreateOrder_ThenReadIt_ThroughTheRedisTheAppHostWired()
    {
        using var created = await _http.PostAsJsonAsync("/api/orders", new { ProductId = 7, Quantity = 2 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var order = await _http.GetFromJsonAsync<Order>(created.Headers.Location);

        Assert.Equal(7, order!.ProductId);
    }
}
```

Run by CI with the real orchestrator: the test project references the AppHost, and `Projects.AppHost`
is generated from that reference. The clients `CreateHttpClient` hands out have no retries, so there's
nothing to turn off. (`MaxRetryAttempts = 0` isn't a way to do it: tested, it's rejected with
`OptionsValidationException` when the client is created.)

## External services and other containers

A service outside the app (a legacy API) comes in as `builder.AddConnectionString("legacy-api")`, read from
configuration; any other image as `builder.AddContainer("name", "image")` with its endpoint. Both are passed
to projects with `WithReference`, like the resources above.
