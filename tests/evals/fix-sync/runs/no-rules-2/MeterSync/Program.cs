using MeterSync.Api;
using MeterSync.Application;
using MeterSync.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<SyncDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Sync")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PatientDirectory>();
builder.Services.AddScoped<MeterSyncService>();
builder.Services.AddSingleton<AlertSweeper>();
builder.Services.AddHostedService<SyncWorker>();

// Without a trailing slash a relative path would replace the last segment ("/v2") of the base address.
static Uri BaseAddress(string value) => new(value.EndsWith('/') ? value : value + "/");

// Reads are safe to retry. The SMS gateway has no idempotency key, so a retry could text the patient twice: no resilience handler there.
builder.Services.AddHttpClient<MeterVendorClient>(http =>
        http.BaseAddress = BaseAddress(builder.Configuration["MeterVendor:BaseAddress"]!))
    .AddStandardResilienceHandler();
builder.Services.AddHttpClient<SmsClient>(http =>
{
    http.BaseAddress = BaseAddress(builder.Configuration["Sms:BaseAddress"]!);
    http.Timeout = TimeSpan.FromSeconds(20);
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Authority = builder.Configuration["Auth:Authority"];
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = true,
            ValidAudience = "meter-sync",
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// The clinic dashboard calls this API from the browser.
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .AllowCredentials()
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapMeterEndpoints();

app.Run();

public partial class Program;
