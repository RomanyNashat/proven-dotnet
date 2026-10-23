using MeterSync.Api;
using MeterSync.Application;
using MeterSync.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Catch a singleton holding a scoped service (a DbContext) at start-up in every environment, not only Development.
builder.Host.UseDefaultServiceProvider(o =>
{
    o.ValidateScopes = true;
    o.ValidateOnBuild = true;
});

// Settings are read inside the registration callbacks, not here: a host built by a test (or any later
// configuration source) adds its values when the host is built, after this file has run.
builder.Services.AddDbContext<SyncDbContext>(o => o.UseNpgsql(
    builder.Configuration.GetConnectionString("Sync") is { Length: > 0 } sync
        ? sync
        : throw new InvalidOperationException("ConnectionStrings:Sync is required.")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<PatientDirectory>();
builder.Services.AddScoped<MeterSyncService>();
builder.Services.AddSingleton<AlertSweeper>();
builder.Services.AddHostedService<SyncWorker>();

// GET only: the standard handler's retries are safe here.
builder.Services.AddHttpClient<MeterVendorClient>(http =>
        http.BaseAddress = BaseAddress.From(builder.Configuration["MeterVendor:BaseAddress"], "MeterVendor:BaseAddress"))
    .AddStandardResilienceHandler();

// No retry handler: every POST to the gateway sends a text and it has no idempotency key. A timeout
// well under the default 100 s, and under AlertSweeper.ClaimLease.
builder.Services.AddHttpClient<SmsClient>(http =>
{
    http.BaseAddress = BaseAddress.From(builder.Configuration["Sms:BaseAddress"], "Sms:BaseAddress");
    http.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Authority = builder.Configuration["Auth:Authority"];
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

// The clinic dashboard calls this API from the browser. Only the configured origins: allowing any
// origin with credentials lets every website make calls with a signed-in user's credentials.
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .AllowCredentials()
    .WithHeaders("Authorization", "Content-Type")
    .WithMethods("GET", "POST")));

builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapMeterEndpoints();

app.Run();

public partial class Program;
