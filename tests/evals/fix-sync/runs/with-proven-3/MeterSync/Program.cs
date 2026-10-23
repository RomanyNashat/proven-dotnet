using System.Data.Common;
using MeterSync.Api;
using MeterSync.Application;
using MeterSync.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// The host checks lifetimes only in Development by default. On everywhere, a singleton holding a scoped
// service (a DbContext) stops the deploy at start-up instead of sharing one context across requests.
builder.Host.UseDefaultServiceProvider(o =>
{
    o.ValidateScopes = true;
    o.ValidateOnBuild = true;
});

builder.Services.AddDbContext<SyncDbContext>((sp, o) =>
    o.UseNpgsql(PooledConnectionString(sp.GetRequiredService<IConfiguration>().GetConnectionString("Sync"))));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<PatientDirectory>();
builder.Services.AddScoped<MeterSyncService>();
builder.Services.AddSingleton<AlertSweeper>();
builder.Services.AddHostedService<SyncWorker>();

builder.Services.AddMeterVendorClient();
builder.Services.AddSmsClient();

builder.Services.AddOptions<AuthSettings>().BindConfiguration("Auth").ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Authority = builder.Configuration["Auth:Authority"];
        o.Audience = AuthSettings.Audience;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            // The authority is a shared realm: without the audience, a token issued to any other client
            // there would be accepted here.
            ValidateAudience = true,
            ValidAudience = AuthSettings.Audience,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// The clinic dashboard calls this API from the browser, with a bearer token: no cookies, so no credentials.
builder.Services.AddCors();
builder.Services.AddOptions<CorsOptions>().Configure<IConfiguration>((o, config) =>
    o.AddDefaultPolicy(p => p
        .WithOrigins(config.GetSection("Cors:Origins").Get<string[]>() ?? [])
        .WithMethods("GET", "POST")
        .WithHeaders("Authorization", "Content-Type")));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<UnexpectedExceptionHandler>();
builder.Services.AddHsts(o =>
{
    o.MaxAge = TimeSpan.FromDays(365);
    o.IncludeSubDomains = true;
});

// nginx terminates TLS: take the original scheme from it, so HSTS is sent. Only the scheme is read, so a
// forged header can't change the client address anything else relies on.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});
builder.Services.AddMeterSyncRateLimits();

var app = builder.Build();

app.UseForwardedHeaders();
app.UseApiSecurityHeaders();
app.UseExceptionHandler();
app.UseHsts();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapMeterEndpoints();

app.Run();

// Several pods share one PostgreSQL (100 connections by default). Npgsql's own default of 100 per pod would
// exhaust it, so a connection string that doesn't set its pool size gets this one. Agree the number with the DBA.
static string PooledConnectionString(string? connectionString)
{
    const int maxPoolSizePerPod = 20;
    var given = new DbConnectionStringBuilder { ConnectionString = connectionString };
    var pooled = new NpgsqlConnectionStringBuilder(connectionString);
    if (!given.ContainsKey("Maximum Pool Size") && !given.ContainsKey("Max Pool Size") && !given.ContainsKey("MaxPoolSize"))
    {
        pooled.MaxPoolSize = maxPoolSizePerPod;
    }

    return pooled.ConnectionString;
}

public partial class Program;
