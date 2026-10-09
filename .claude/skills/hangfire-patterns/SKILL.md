---
name: hangfire-patterns
description: Hangfire for .NET: background jobs, scheduled/recurring jobs, queues, retries, dashboard.
version: 1.0.0
---

# Hangfire Patterns

## When to Use Hangfire vs Quartz vs CronJob

| Use Hangfire | Use Quartz.NET | Use K8s CronJob |
|-------------|----------------|-----------------|
| Fire-and-forget from web requests | Sub-minute precision scheduling | Isolated execution, scale-to-zero |
| Need a visual dashboard | Need clustered in-process scheduling | Data migration jobs |
| Delayed jobs (send email in 30min) | Complex trigger logic (calendars) | Heavy batch processing |
| Job continuations (A then B) | Job chaining with dependencies | Simple scheduled tasks |
| Persistent retry with visibility | High-throughput scheduling | VM/bare-metal cron replacement |

## Setup

### Registration
```csharp
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    // PostgreSQL storage
    .UsePostgreSqlStorage(options =>
    {
        options.UseNpgsqlConnection(
            builder.Configuration.GetConnectionString("Hangfire")!);
    })
    // Or SQL Server storage
    // .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
    // {
    //     CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
    //     SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
    //     QueuePollInterval = TimeSpan.Zero,
    //     UseRecommendedIsolationLevel = true,
    //     PrepareSchemaIfNecessary = true
    // })
);

builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = Environment.ProcessorCount * 2;
    options.Queues = ["critical", "default", "low"];
    options.ServerName = $"{Environment.MachineName}:{Process.GetCurrentProcess().Id}";
});
```

### Dashboard (secured)
```csharp
// Only accessible to admins
app.MapHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new HangfireDashboardAuthFilter()],
    DashboardTitle = "Order Service Jobs",
    DisplayStorageConnectionString = false
});

// Auth filter
public sealed class HangfireDashboardAuthFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        return httpContext.User.IsInRole("Admin");
    }
}
```

### NuGet Packages
```xml
<PackageReference Include="Hangfire.Core" Version="1.8.17" />
<PackageReference Include="Hangfire.AspNetCore" Version="1.8.17" />
<PackageReference Include="Hangfire.PostgreSql" Version="1.20.10" />
<!-- Or: Hangfire.SqlServer for SQL Server -->
```

## Job Types

### Fire-and-Forget (enqueue immediately)
```csharp
// From a controller or service — job runs in background, request returns immediately
public sealed class OrderService(IBackgroundJobClient jobClient)
{
    public async Task<OrderDto> CreateOrderAsync(CreateOrderCommand cmd, CancellationToken ct)
    {
        var order = Order.Create(cmd.CustomerId, cmd.Items);
        await _repository.AddAsync(order, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        // Fire-and-forget: send confirmation email
        jobClient.Enqueue<IEmailService>(
            service => service.SendOrderConfirmationAsync(order.Id, CancellationToken.None));

        // Fire-and-forget: sync to analytics
        jobClient.Enqueue<IAnalyticsService>(
            service => service.TrackOrderCreatedAsync(order.Id, CancellationToken.None));

        return order.ToDto();
    }
}
```

### Delayed (schedule for later)
```csharp
// Send a review request email 24 hours after delivery
jobClient.Schedule<IEmailService>(
    service => service.SendReviewRequestAsync(orderId, CancellationToken.None),
    TimeSpan.FromHours(24));

// Send at a specific time
jobClient.Schedule<IReminderService>(
    service => service.SendReminderAsync(userId, CancellationToken.None),
    new DateTimeOffset(2026, 3, 25, 9, 0, 0, TimeSpan.FromHours(3)));  // 9 AM Riyadh
```

### Recurring (cron-based)
```csharp
// Register recurring jobs at startup
RecurringJob.AddOrUpdate<IDailyReportService>(
    "daily-report",
    service => service.GenerateAsync(CancellationToken.None),
    Cron.Daily(2, 0),  // 2:00 AM UTC
    new RecurringJobOptions
    {
        TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Arab Standard Time"),
        Queue = "default"
    });

RecurringJob.AddOrUpdate<ILeaderboardService>(
    "leaderboard-sync",
    service => service.SyncAllAsync(CancellationToken.None),
    "*/5 * * * *",  // every 5 minutes
    new RecurringJobOptions { Queue = "critical" });

RecurringJob.AddOrUpdate<IRetentionService>(
    "cleanup-old-logs",
    service => service.CleanupAsync(90, CancellationToken.None),  // 90 days
    Cron.Weekly(DayOfWeek.Sunday, 3, 0),  // Sunday 3 AM
    new RecurringJobOptions { Queue = "low" });
```

### Continuations (job chains)
```csharp
// Job B runs only after Job A succeeds
var jobA = jobClient.Enqueue<IOrderProcessor>(
    service => service.ValidateOrderAsync(orderId, CancellationToken.None));

var jobB = jobClient.ContinueJobWith<IPaymentProcessor>(
    jobA,
    service => service.ProcessPaymentAsync(orderId, CancellationToken.None));

var jobC = jobClient.ContinueJobWith<INotificationService>(
    jobB,
    service => service.SendPaymentConfirmationAsync(orderId, CancellationToken.None));
```

### Batch Jobs (Hangfire.Pro feature)
```csharp
// If using Hangfire Pro — process a batch then run continuation
BatchJob.StartNew(batch =>
{
    foreach (var orderId in orderIds)
    {
        batch.Enqueue<IOrderExporter>(
            service => service.ExportAsync(orderId, CancellationToken.None));
    }
}, continuation =>
{
    continuation.Enqueue<IReportService>(
        service => service.GenerateExportReportAsync(CancellationToken.None));
});
```

## Job Implementation Best Practices

```csharp
// Jobs should be idempotent — safe to retry
public sealed class SendOrderConfirmationJob(
    IOrderRepository orderRepository,
    IEmailSender emailSender,
    ILogger<SendOrderConfirmationJob> logger) : IEmailService
{
    // Hangfire calls this method — parameters are serialized to storage
    [AutomaticRetry(Attempts = 5, DelaysInSeconds = [10, 30, 60, 300, 900])]
    [Queue("default")]
    public async Task SendOrderConfirmationAsync(Guid orderId, CancellationToken ct)
    {
        var order = await orderRepository.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            logger.LogWarning("Order {OrderId} not found, skipping email", orderId);
            return;  // don't throw — nothing to retry
        }

        // Idempotency check — don't send duplicate emails
        if (order.ConfirmationEmailSentAt is not null)
        {
            logger.LogInformation("Confirmation already sent for {OrderId}", orderId);
            return;
        }

        await emailSender.SendAsync(new OrderConfirmationEmail(order));

        order.MarkConfirmationEmailSent();
        await orderRepository.UpdateAsync(order, ct);

        logger.LogInformation("Order confirmation sent for {OrderId}", orderId);
    }
}
```

## Retry and Error Handling

```csharp
// Global retry filter
builder.Services.AddHangfire(config => config
    .UseFilter(new AutomaticRetryAttribute
    {
        Attempts = 3,
        DelaysInSeconds = [30, 120, 600],  // 30s, 2min, 10min
        OnAttemptsExceeded = AttemptsExceededAction.Fail
    }));

// Per-job retry
[AutomaticRetry(Attempts = 10, DelaysInSeconds = [60, 300, 900, 3600])]
public async Task ProcessCriticalPaymentAsync(Guid paymentId, CancellationToken ct)
{
    // ...
}

// Disable retry for non-retryable jobs
[AutomaticRetry(Attempts = 0)]
public async Task SendOneTimeNotificationAsync(Guid userId, CancellationToken ct)
{
    // ...
}
```

## Queue Priority

```csharp
// Server processes queues in order — critical first
builder.Services.AddHangfireServer(options =>
{
    options.Queues = ["critical", "default", "low"];
});

// Enqueue to specific queue
jobClient.Enqueue<IPaymentService>(
    service => service.ProcessRefundAsync(orderId, CancellationToken.None));
// Queue attribute on the method: [Queue("critical")]

// Or via attribute on the job class method
[Queue("critical")]
[AutomaticRetry(Attempts = 5)]
public async Task ProcessRefundAsync(Guid orderId, CancellationToken ct) { }
```

## Monitoring Endpoints

```csharp
// Health check for Hangfire
builder.Services.AddHealthChecks()
    .AddHangfire(options =>
    {
        options.MaximumJobsFailed = 10;
        options.MinimumAvailableServers = 1;
    }, name: "hangfire", tags: ["ready"]);
```

## Testing Hangfire Jobs

```csharp
// Test the job logic directly — no Hangfire infrastructure needed
[Fact]
public async Task SendOrderConfirmation_OrderExists_SendsEmail()
{
    // Arrange
    var order = TestOrderBuilder.CreatePending();
    var repository = new Mock<IOrderRepository>();
    repository
        .Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>()))
        .ReturnsAsync(order);

    var emailSender = new Mock<IEmailSender>();
    var logger = new Mock<ILogger<SendOrderConfirmationJob>>();

    var job = new SendOrderConfirmationJob(repository.Object, emailSender.Object, logger.Object);

    // Act
    await job.SendOrderConfirmationAsync(order.Id, CancellationToken.None);

    // Assert
    emailSender.Verify(e => e.SendAsync(It.IsAny<OrderConfirmationEmail>()), Times.Once);
}

[Fact]
public async Task SendOrderConfirmation_AlreadySent_DoesNotSendAgain()
{
    var order = TestOrderBuilder.CreatePending();
    order.MarkConfirmationEmailSent();  // already sent

    var repository = new Mock<IOrderRepository>();
    repository
        .Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>()))
        .ReturnsAsync(order);

    var emailSender = new Mock<IEmailSender>();

    var job = new SendOrderConfirmationJob(repository.Object, emailSender.Object,
        new Mock<ILogger<SendOrderConfirmationJob>>().Object);

    await job.SendOrderConfirmationAsync(order.Id, CancellationToken.None);

    emailSender.Verify(e => e.SendAsync(It.IsAny<OrderConfirmationEmail>()), Times.Never);
}
```
