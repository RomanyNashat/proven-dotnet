using Confluent.Kafka;

namespace Appointments;

/// <summary>Reminder requests from the scheduling service: each one becomes a Hangfire job.</summary>
public sealed class ReminderRequestsConsumer(IConfiguration config, IServiceScopeFactory scopes, ILogger<ReminderRequestsConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = config["Kafka:BootstrapServers"],
            GroupId = "appointments-reminders",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true,
        }).Build();
        consumer.Subscribe("reminders.requested");

        while (!stoppingToken.IsCancellationRequested)
        {
            var result = consumer.Consume(stoppingToken);
            using var scope = scopes.CreateScope();
            var request = System.Text.Json.JsonSerializer.Deserialize<ReminderRequested>(result.Message.Value)!;
            scope.ServiceProvider.GetRequiredService<ReminderScheduler>().Schedule(request.AppointmentId, request.SendAt);
            logger.LogInformation("Reminder scheduled for {AppointmentId}", request.AppointmentId);
        }
    }
}

public sealed record ReminderRequested(int AppointmentId, DateTimeOffset SendAt);

/// <summary>Payment confirmations: marks the appointment paid.</summary>
public sealed class PaymentEventsConsumer(IConfiguration config, IServiceScopeFactory scopes) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = config["Kafka:BootstrapServers"],
            GroupId = "appointments-payments",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true,
            EnableAutoOffsetStore = false,
        }).Build();
        consumer.Subscribe("payments.confirmed");

        while (!stoppingToken.IsCancellationRequested)
        {
            var result = consumer.Consume(stoppingToken);
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppointmentsDbContext>();
            var paid = System.Text.Json.JsonSerializer.Deserialize<PaymentConfirmed>(result.Message.Value)!;
            await db.MarkPaidAsync(paid.AppointmentId, paid.PaymentId, stoppingToken);   // idempotent on PaymentId
            consumer.StoreOffset(result);
        }
    }
}

public sealed record PaymentConfirmed(int AppointmentId, string PaymentId);
