using MeterSync.Api;
using MeterSync.Application;
using MeterSync.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// Settings are read when the services are built, not here, so host-level overrides
// (environment, WebApplicationFactory) are always seen.
builder.Services.AddDbContext<SyncDbContext>((sp, o) => o.UseNpgsql(
    sp.GetRequiredService<IConfiguration>().GetConnectionString("Sync") is { Length: > 0 } cs
        ? cs
        : throw new InvalidOperationException("ConnectionStrings:Sync is not set.")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<PatientDirectory>();
builder.Services.AddScoped<MeterSyncService>();
builder.Services.AddScoped<AlertSweeper>();
builder.Services.AddHostedService<SyncWorker>();

// GETs are safe to retry, so the vendor client gets the standard pipeline (retries, breaker, timeouts).
builder.Services.AddHttpClient<MeterVendorClient>((sp, http) =>
        http.BaseAddress = ServiceAddress(sp.GetRequiredService<IConfiguration>(), "MeterVendor:BaseAddress"))
    .AddStandardResilienceHandler();

// No retries: the gateway has no idempotency key, so a retried POST is a second SMS to the patient.
builder.Services.AddHttpClient<SmsClient>((sp, http) =>
{
    http.BaseAddress = ServiceAddress(sp.GetRequiredService<IConfiguration>(), "Sms:BaseAddress");
    http.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Authority = config["Auth:Authority"];
        o.RequireHttpsMetadata = true;
        o.Audience = "meter-sync";
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = true,
            ValidAudience = "meter-sync",
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// The clinic dashboard calls this API from the browser: only its origins, never "any origin with credentials".
builder.Services.AddCors();
builder.Services.AddOptions<Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions>()
    .Configure<IConfiguration>((o, cfg) => o.AddDefaultPolicy(p => p
        .WithOrigins(cfg.GetSection("Cors:Origins").Get<string[]>() ?? [])
        .AllowCredentials()
        .WithHeaders("Authorization", "Content-Type")
        .WithMethods("GET", "POST")));

builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapMeterEndpoints();

app.Run();

// A relative path resolves against the base address only up to its last "/": without a trailing slash,
// "https://meters.example.net/v2" + "meters/x" becomes "https://meters.example.net/meters/x".
static Uri ServiceAddress(IConfiguration config, string key)
{
    var value = config[key];
    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
    {
        throw new InvalidOperationException($"{key} must be an absolute https URL.");
    }

    return uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
}

public partial class Program;
