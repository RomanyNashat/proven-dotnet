using MeterSync.Api;
using MeterSync.Application;
using MeterSync.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Read when the client is built, not here, so configuration added by the host (tests included) is seen.
static Uri BaseAddress(IServiceProvider services, string key) =>
    services.GetRequiredService<IConfiguration>()[key] is { Length: > 0 } value
        ? MeterVendorClient.BaseAddressFrom(value)
        : throw new InvalidOperationException($"The setting {key} is required.");

builder.Services.AddDbContext<SyncDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Sync")));
builder.Services.AddSingleton(TimeProvider.System);

// Scoped: it holds the scoped DbContext (as a singleton it captured one context for every request, across threads).
builder.Services.AddScoped<PatientDirectory>();
builder.Services.AddScoped<MeterSyncService>();
builder.Services.AddSingleton<AlertSweeper>();
builder.Services.AddHostedService<SyncWorker>();

builder.Services.AddHttpClient<MeterVendorClient>((services, http) =>
        http.BaseAddress = BaseAddress(services, "MeterVendor:BaseAddress"))
    .AddStandardResilienceHandler();

// No retry handler: the gateway has no idempotency key, so a retried POST is a second SMS. A timeout instead
// of the 100 s default, kept well under the sweeper's claim lease.
builder.Services.AddHttpClient<SmsClient>((services, http) =>
{
    http.BaseAddress = BaseAddress(services, "Sms:BaseAddress");
    http.Timeout = TimeSpan.FromSeconds(20);
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Authority = builder.Configuration["Auth:Authority"];
        o.Audience = "meter-sync";
        o.TokenValidationParameters = new TokenValidationParameters
        {
            // Without this, a token the identity provider issued for any other app was accepted here.
            ValidateAudience = true,
            ValidAudience = "meter-sync",
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// The clinic dashboard calls this API from the browser. Only its origins, and no credentials: the API
// takes bearer tokens, not cookies. Any origin with credentials let every site call it as the user.
builder.Services.AddCors();
builder.Services.AddOptions<CorsOptions>().Configure<IConfiguration>((o, configuration) =>
    o.AddDefaultPolicy(p => p
        .WithOrigins(configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
        .AllowAnyHeader()
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

public partial class Program;
