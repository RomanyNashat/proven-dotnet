using Appointments;
using Confluent.Kafka;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

// Runs as 4 replicas behind the ingress.
var builder = WebApplication.CreateBuilder(args);
var postgres = builder.Configuration.GetConnectionString("Postgres")!;
var redis = builder.Configuration.GetConnectionString("Redis")!;

builder.Services.AddDbContext<AppointmentsDbContext>(o => o.UseNpgsql(postgres));
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redis));
builder.Services.AddStackExchangeRedisCache(o => o.Configuration = redis);
builder.Services.AddHybridCache();
builder.Services.AddSingleton(new ProducerBuilder<string, string>(new ProducerConfig
{
    BootstrapServers = builder.Configuration["Kafka:BootstrapServers"],
    Acks = Acks.Leader,
}).Build());
builder.Services.AddHostedService<ReminderRequestsConsumer>();
builder.Services.AddHostedService<PaymentEventsConsumer>();
builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<SlotLock>();
builder.Services.AddScoped<ClinicPrices>();
builder.Services.AddScoped<IReminderSender, ReminderSender>();
builder.Services.AddScoped<ReminderScheduler>();
builder.Services.AddHttpClient<ISmsGateway, SmsGateway>(c => c.BaseAddress = new Uri(builder.Configuration["Sms:Url"]!));
builder.Services.AddSingleton<OtpRateLimiter>();
builder.Services.AddHangfire(c => c.UsePostgreSqlStorage(o => o.UseNpgsqlConnection(postgres)));
builder.Services.AddHangfireServer();
builder.Services.AddGrpc(o => o.Interceptors.Add<ErrorInterceptor>());
builder.Services.AddGrpcClient<Doctors.DoctorsClient>(o => o.Address = new Uri(builder.Configuration["Doctors:Url"]!));
builder.Services.AddSignalR().AddStackExchangeRedis(redis);
builder.Services.AddAuthentication().AddJwtBearer();
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapHub<VisitsHub>("/hubs/visits").RequireAuthorization();
app.MapGrpcService<AppointmentsGrpcService>();
app.MapPost("/appointments", async (BookAppointment request, BookingService booking, CancellationToken ct) =>
    TypedResults.Ok(await booking.BookAsync(request, ct))).RequireAuthorization();
app.MapPut("/clinics/{clinicId:int}/price", async (int clinicId, decimal price, ClinicPrices prices, CancellationToken ct) =>
{
    await prices.UpdateAsync(clinicId, price, ct);
    return TypedResults.NoContent();
}).RequireAuthorization("Admin");
app.Run();
