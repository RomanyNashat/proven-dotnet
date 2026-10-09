using ClinicBooking.Application;
using ClinicBooking.Domain;
using ClinicBooking.Infrastructure;
using ClinicBooking.Notifications;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<ClinicDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
builder.Services.AddHybridCache();
builder.Services.AddScoped<SqlClinicDirectory>();
builder.Services.AddScoped<IClinicDirectory>(sp => new CachedClinicDirectory(
    sp.GetRequiredService<SqlClinicDirectory>(), sp.GetRequiredService<Microsoft.Extensions.Caching.Hybrid.HybridCache>()));
builder.Services.AddSingleton<IProducer<string, string>>(_ => new ProducerBuilder<string, string>(new ProducerConfig
{
    BootstrapServers = builder.Configuration["Kafka:BootstrapServers"],
    MessageSendMaxRetries = RetryStrategy.MaxAttempts,
    RetryBackoffMs = (int)RetryStrategy.Backoff.TotalMilliseconds,
}).Build());
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<PricingService>();
builder.Services.AddScoped<EligibilityChecker>();
builder.Services.AddScoped<ClaimExporter>();
builder.Services.AddScoped<BookAppointmentHandler>();
builder.Services.AddScoped<CheckInHandler>();
builder.Services.AddScoped<CancelHandler>();
builder.Services.AddScoped<CompleteHandler>();
builder.Services.AddScoped<TodayListHandler>();
builder.Services.AddScoped<INotificationChannel, SmsChannel>();
builder.Services.AddScoped<INotificationChannel, EmailChannel>();
builder.Services.AddScoped<ReminderSender>();

var app = builder.Build();
app.MapGet("/clinics/{city}", async (string city, IClinicDirectory clinics, CancellationToken ct) => TypedResults.Ok(await clinics.InCityAsync(city, ct)));
app.MapPost("/appointments", async (BookAppointment command, BookAppointmentHandler handler, CancellationToken ct) => TypedResults.Ok(await handler.HandleAsync(command, ct)));
app.MapGet("/clinics/{clinicId:int}/today", (int clinicId, TodayListHandler handler) =>
{
    var max = ClinicSettingsCache.Instance.GetInt("MaxBookingsPerDay");
    var list = handler.Handle(clinicId);
    return TypedResults.Ok(new { list, full = list.Count >= max });
});
app.Run();
