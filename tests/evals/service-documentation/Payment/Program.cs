using Payment;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration["Redis:Connection"]!));
builder.Services.AddOptions<PaymentOptions>().BindConfiguration("Payments").ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddHttpClient<GatewayClient>(http => http.BaseAddress = new Uri(builder.Configuration["Gateway:BaseUrl"]!))
    .AddStandardResilienceHandler();
builder.Services.AddScoped<PaymentBatchService>();
builder.Services.AddAuthentication().AddJwtBearer();
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

var payments = app.MapGroup("/api/payments").RequireAuthorization("PaymentsWriter");
payments.MapPost("/batch", PaymentEndpoints.SubmitBatch);
payments.MapGet("/{id}", PaymentEndpoints.GetPayment);

app.Run();
