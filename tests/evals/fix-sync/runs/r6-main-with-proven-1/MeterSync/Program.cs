using MeterSync.Api;
using MeterSync.Application;
using MeterSync.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

const string Audience = "meter-sync";

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// Catch captive dependencies (a singleton holding a scoped DbContext) at startup, in every environment.
builder.Host.UseDefaultServiceProvider(o =>
{
    o.ValidateScopes = true;
    o.ValidateOnBuild = true;
});

builder.Services.AddDbContext<SyncDbContext>(o => o.UseNpgsql(config.GetConnectionString("Sync")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<PatientDirectory>();
builder.Services.AddScoped<MeterSyncService>();
builder.Services.AddScoped<AlertSweeper>();
builder.Services.AddHostedService<SyncWorker>();

builder.Services.AddHttpClient<MeterVendorClient>(http =>
        http.BaseAddress = BaseAddress.WithTrailingSlash(config["MeterVendor:BaseAddress"], "MeterVendor:BaseAddress"))
    .AddStandardResilienceHandler();

// No retry handler: the gateway has no idempotency key, so a retried POST can text the patient twice.
builder.Services.AddHttpClient<SmsClient>(http =>
{
    http.BaseAddress = BaseAddress.WithTrailingSlash(config["Sms:BaseAddress"], "Sms:BaseAddress");
    http.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Authority = config["Auth:Authority"];
        o.Audience = Audience;
        o.RequireHttpsMetadata = true;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = true,
            ValidAudience = Audience,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// The clinic dashboard calls this API from the browser with a bearer token (no cookies, so no credentials).
builder.Services.AddCors(o =>
{
    var origins = config.GetSection("Cors:Origins").Get<string[]>() ?? [];
    if (origins.Length == 0)
    {
        throw new InvalidOperationException("Cors:Origins must list at least one origin.");
    }

    o.AddDefaultPolicy(p => p
        .WithOrigins(origins)
        .WithMethods("GET", "POST")
        .WithHeaders("Authorization", "Content-Type"));
});

// Builds the CORS options at startup so a missing Cors:Origins fails the deploy, not the first request.
builder.Services.AddOptions<CorsOptions>().ValidateOnStart();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

// No HTTPS redirection: TLS ends at the ingress.
app.UseSecurityHeaders();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapMeterEndpoints();

app.Run();

/// <summary>The entry point; public so the tests can host it.</summary>
public partial class Program;
