using MeterSync.Api;
using MeterSync.Application;
using MeterSync.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.Services.AddDbContext<SyncDbContext>(o => o.UseNpgsql(
    config.GetConnectionString("Sync") ?? throw new InvalidOperationException("ConnectionStrings:Sync is not set.")));
builder.Services.AddSingleton(TimeProvider.System);
// Scoped: it holds the scoped DbContext. As a singleton it shared one DbContext across all requests.
builder.Services.AddScoped<PatientDirectory>();
builder.Services.AddScoped<MeterSyncService>();
builder.Services.AddSingleton<AlertSweeper>();
builder.Services.AddHostedService<SyncWorker>();

// GETs are safe to retry, so the vendor gets the standard pipeline (retries, circuit breaker, timeouts).
builder.Services.AddHttpClient<MeterVendorClient>(http => http.BaseAddress = DirectoryUri(config, "MeterVendor:BaseAddress"))
    .AddStandardResilienceHandler();
// No retries: every POST to the gateway sends an SMS and it has no idempotency key. Only a timeout.
builder.Services.AddHttpClient<SmsClient>(http =>
{
    http.BaseAddress = DirectoryUri(config, "Sms:BaseAddress");
    http.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Authority = config["Auth:Authority"];
        o.Audience = "meter-sync";
        o.TokenValidationParameters.ValidateAudience = true;
        o.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
    });
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// The clinic dashboard calls this API from the browser. Only its origins; bearer tokens, so no cookies.
var origins = config.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(origins)
    .WithHeaders("Authorization", "Content-Type")
    .WithMethods("GET", "POST")));

builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapMeterEndpoints();

app.Run();

// HttpClient resolves "meters/x" against "https://host/v2" as "https://host/meters/x", dropping /v2.
// The base must end in a slash for relative paths to land under it.
static Uri DirectoryUri(IConfiguration config, string key)
{
    var value = config[key];
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException($"{key} is not set.");
    }

    return new Uri(value.EndsWith('/') ? value : value + "/", UriKind.Absolute);
}

public partial class Program;
