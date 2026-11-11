using MeterSync.Api;
using MeterSync.Application;
using MeterSync.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.Services.AddProblemDetails();

builder.Services.AddDbContext<SyncDbContext>(o => o.UseNpgsql(config.GetConnectionString("Sync")));
builder.Services.AddSingleton(TimeProvider.System);
// Scoped: it holds a DbContext, which is scoped and not thread-safe. As a singleton every request shared one.
builder.Services.AddScoped<PatientDirectory>();
builder.Services.AddScoped<MeterSyncService>();
builder.Services.AddSingleton<AlertSweeper>();
builder.Services.AddHostedService<SyncWorker>();

builder.Services.AddHttpClient<MeterVendorClient>(http =>
        http.BaseAddress = ServiceAddress.From(Required(config, "MeterVendor:BaseAddress")))
    .AddStandardResilienceHandler();
// No resilience handler on purpose: the gateway has no idempotency key, so a retry can send the SMS twice.
builder.Services.AddHttpClient<SmsClient>(http =>
{
    http.BaseAddress = ServiceAddress.From(Required(config, "Sms:BaseAddress"));
    http.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Authority = config["Auth:Authority"];
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

// The clinic dashboard calls this API from the browser. Only the configured origins; the API uses bearer
// tokens, not cookies, so credentials aren't needed.
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(config.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.Use((context, next) =>
{
    var headers = context.Response.Headers;
    headers.XContentTypeOptions = "nosniff";
    headers.XFrameOptions = "DENY";
    headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
    return next(context);
});

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapMeterEndpoints();

app.Run();

static string Required(IConfiguration config, string key) =>
    config[key] is { Length: > 0 } value ? value : throw new InvalidOperationException($"Configuration '{key}' is required.");

public partial class Program;
