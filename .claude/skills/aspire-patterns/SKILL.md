---
name: aspire-patterns
description: .NET Aspire: AppHost composition, service discovery, resource wiring, ServiceDefaults telemetry.
version: 2.0.0
---

# .NET Aspire Patterns

## AppHost (Orchestration)

```csharp
// AppHost/Program.cs — defines the entire distributed application
var builder = DistributedApplication.CreateBuilder(args);

// Infrastructure
var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin()
    .WithDataVolume("postgres-data");

var orderDb = postgres.AddDatabase("orderdb");
var inventoryDb = postgres.AddDatabase("inventorydb");

// SQL Server instead (Aspire.Hosting.SqlServer):
// var sql = builder.AddSqlServer("sql").WithDataVolume("sql-data");
// var orderDb = sql.AddDatabase("orderdb");

var redis = builder.AddRedis("redis")
    .WithRedisCommander()
    .WithDataVolume("redis-data");

var kafka = builder.AddKafka("kafka")
    .WithKafkaUI();

var mongo = builder.AddMongoDB("mongodb")
    .WithMongoExpress()
    .AddDatabase("notificationdb");

// Services
var orderService = builder.AddProject<Projects.OrderService_Api>("order-service")
    .WithReference(orderDb)
    .WithReference(redis)
    .WithReference(kafka)
    .WaitFor(orderDb)
    .WaitFor(redis);

var inventoryService = builder.AddProject<Projects.InventoryService_Api>("inventory-service")
    .WithReference(inventoryDb)
    .WithReference(kafka)
    .WaitFor(inventoryDb);

var notificationService = builder.AddProject<Projects.NotificationService_Api>("notification-service")
    .WithReference(mongo)
    .WithReference(redis)
    .WithReference(kafka)
    .WaitFor(mongo);

// API Gateway
builder.AddProject<Projects.ApiGateway>("api-gateway")
    .WithReference(orderService)
    .WithReference(inventoryService)
    .WithReference(notificationService)
    .WithExternalHttpEndpoints();

builder.Build().Run();
```

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
```csharp
// OrderService.Api/Program.cs
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();  // OpenTelemetry, health checks, HTTP defaults

// Aspire components — auto-configured from AppHost references
builder.AddNpgsqlDbContext<AppDbContext>("orderdb");      // SQL Server: AddSqlServerDbContext<AppDbContext>("orderdb")
builder.AddRedisDistributedCache("redis");
builder.AddKafkaProducer<string, string>("kafka");

var app = builder.Build();
app.MapDefaultEndpoints();  // /health/live, /health/ready
```

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

```csharp
public sealed class OrderApiTests : IAsyncLifetime
{
    private DistributedApplication _app = null!;
    private HttpClient _httpClient = null!;

    public async Task InitializeAsync()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.AppHost>();

        // Override settings for test environment
        appHost.Services.ConfigureHttpClientDefaults(http =>
            http.AddStandardResilienceHandler(options =>
                options.Retry.MaxRetryAttempts = 0));  // no retries in tests

        _app = await appHost.BuildAsync();
        await _app.StartAsync();

        var resourceNotification = _app.Services
            .GetRequiredService<ResourceNotificationService>();

        // Wait for order-service to be running
        await resourceNotification.WaitForResourceAsync(
            "order-service",
            KnownResourceStates.Running)
            .WaitAsync(TimeSpan.FromSeconds(60));

        _httpClient = _app.CreateHttpClient("order-service");
    }

    [Fact]
    public async Task CreateOrder_ValidRequest_ReturnsCreated()
    {
        var request = new { Items = new[] { new { ProductId = 7, Quantity = 2 } } };

        var response = await _httpClient.PostAsJsonAsync("/api/orders", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
    }
}
```

## Custom Resource (external service)

```csharp
// Add a service that isn't a .NET project
var legacyApi = builder.AddConnectionString("legacy-api");

// Or a container-based service
var seq = builder.AddContainer("seq", "datalust/seq")
    .WithHttpEndpoint(port: 5341, targetPort: 80)
    .WithEnvironment("ACCEPT_EULA", "Y");

// Wire to services
orderService.WithReference(legacyApi);
orderService.WithReference(seq);
```
