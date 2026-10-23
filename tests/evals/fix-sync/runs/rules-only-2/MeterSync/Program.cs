using MeterSync.Api;
using MeterSync.Application;
using MeterSync.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Check lifetimes in every environment, not only Development: a singleton holding a DbContext
// otherwise only shows up in production, as one context shared by every request.
builder.Host.UseDefaultServiceProvider(o =>
{
    o.ValidateScopes = true;
    o.ValidateOnBuild = true;
});

builder.Services.AddProblemDetails();

builder.Services.AddDbContext<SyncDbContext>((sp, o) =>
    o.UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Sync")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<PatientDirectory>();
builder.Services.AddScoped<MeterSyncService>();
builder.Services.AddSingleton<AlertSweeper>();
builder.Services.AddHostedService<SyncWorker>();

// GETs only: the standard handler's retries are safe here.
builder.Services.AddHttpClient<MeterVendorClient>((sp, http) =>
        http.BaseAddress = BaseAddress.From(sp.GetRequiredService<IConfiguration>()["MeterVendor:BaseAddress"], "MeterVendor:BaseAddress"))
    .AddStandardResilienceHandler();

// No retries: the gateway has no idempotency key, so a retried POST is a second text to the patient.
// The timeout stays well inside AlertSweeper's claim lease.
builder.Services.AddHttpClient<SmsClient>((sp, http) =>
{
    http.BaseAddress = BaseAddress.From(sp.GetRequiredService<IConfiguration>()["Sms:BaseAddress"], "Sms:BaseAddress");
    http.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IConfiguration>((o, config) =>
    {
        o.Authority = config["Auth:Authority"];
        o.Audience = "meter-sync";
        // Without the audience check, a token the same identity provider issued for any other app is
        // accepted here.
        o.TokenValidationParameters.ValidateAudience = true;
        o.TokenValidationParameters.ValidAudience = "meter-sync";
        o.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
    });
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// The clinic dashboard calls this API from the browser. Only its origins: allowing every origin let any
// website a signed-in patient visited call the API in their name. The token travels in the Authorization
// header, so no credentials (cookies) are needed.
builder.Services.AddCors();
builder.Services.AddOptions<CorsOptions>()
    .Configure<IConfiguration>((o, config) =>
    {
        var origins = config.GetSection("Cors:Origins").Get<string[]>() ?? [];
        o.AddDefaultPolicy(p => p
            .WithOrigins(origins)
            .WithHeaders("Authorization", "Content-Type")
            .WithMethods("GET", "POST"));
    });

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapMeterEndpoints();

app.Run();

public partial class Program;
