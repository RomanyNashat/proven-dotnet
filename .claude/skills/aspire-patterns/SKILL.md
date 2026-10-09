---
name: aspire-patterns
description: .NET Aspire: AppHost composition, service discovery, resource wiring, ServiceDefaults telemetry.
version: 1.0.0
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

```csharp
// ServiceDefaults/Extensions.cs — shared across all services
public static class ServiceDefaultsExtensions
{
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        // OpenTelemetry
        builder.ConfigureOpenTelemetry();

        // Health checks
        builder.AddDefaultHealthChecks();

        // HTTP client defaults
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        // Service discovery
        builder.Services.AddServiceDiscovery();

        return builder;
    }

    private static IHostApplicationBuilder ConfigureOpenTelemetry(
        this IHostApplicationBuilder builder)
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddGrpcClientInstrumentation()
                    .AddEntityFrameworkCoreInstrumentation()
                    .AddSource("Npgsql");   // SQL Server: .AddSqlClientInstrumentation() (OpenTelemetry.Instrumentation.SqlClient)
            });

        builder.AddOpenTelemetryExporters();
        return builder;
    }

    private static IHostApplicationBuilder AddOpenTelemetryExporters(
        this IHostApplicationBuilder builder)
    {
        var useOtlp = !string.IsNullOrWhiteSpace(
            builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlp)
        {
            builder.Services.AddOpenTelemetry()
                .UseOtlpExporter();
        }

        return builder;
    }

    private static IHostApplicationBuilder AddDefaultHealthChecks(
        this IHostApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }
}
```

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
app.MapDefaultEndpoints();  // /health/live, /health/ready, /alive
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

// The "https+http://" scheme tells Aspire to:
// 1. Try HTTPS first
// 2. Fall back to HTTP if HTTPS isn't available
// 3. Resolve the service name via configured discovery provider
```

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
