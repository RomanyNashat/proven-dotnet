using System.Security.Claims;
using System.Threading.RateLimiting;
using MeterSync.Api;
using MeterSync.Application;
using MeterSync.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

const string Audience = "meter-sync";

var builder = WebApplication.CreateBuilder(args);

// The host checks service lifetimes only in Development. Without this, a singleton holding a DbContext
// starts fine in Production and shares one context across every request.
builder.Host.UseDefaultServiceProvider(o =>
{
    o.ValidateScopes = true;
    o.ValidateOnBuild = true;
});

// Settings are read through options, validated at start-up: a deploy missing one stops there, not at the
// first request or the first sync.
builder.Services.AddOptions<SyncDatabaseOptions>()
    .Configure<IConfiguration>((o, config) => o.ConnectionString = config.GetConnectionString("Sync"))
    .PostConfigure(o =>
    {
        if (!string.IsNullOrWhiteSpace(o.ConnectionString))
        {
            o.ConnectionString = SyncDatabase.WithPoolLimit(o.ConnectionString);
        }
    })
    .ValidateDataAnnotations()
    .ValidateOnStart();
AddHttpServiceOptions(builder.Services, HttpServiceOptions.MeterVendor);
AddHttpServiceOptions(builder.Services, HttpServiceOptions.Sms);

builder.Services.AddDbContext<SyncDbContext>((sp, o) =>
    o.UseNpgsql(sp.GetRequiredService<IOptions<SyncDatabaseOptions>>().Value.ConnectionString));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<PatientDirectory>();
builder.Services.AddScoped<MeterSyncService>();
builder.Services.AddSingleton<AlertSweeper>();
builder.Services.AddHostedService<SyncWorker>();

// No default HttpClient logging on either client: it writes the request URI (the meter's serial) and, on
// failure, the exception with its message. The services log their own lines without patient data.
builder.Services.AddHttpClient<MeterVendorClient>((sp, http) => http.BaseAddress = BaseAddressOf(sp, HttpServiceOptions.MeterVendor))
    .RemoveAllLoggers()
    .AddStandardResilienceHandler();

// No retries: every POST sends an SMS and the gateway has no idempotency key.
builder.Services.AddHttpClient<SmsClient>((sp, http) =>
    {
        http.BaseAddress = BaseAddressOf(sp, HttpServiceOptions.Sms);
        http.Timeout = TimeSpan.FromSeconds(10);
    })
    .RemoveAllLoggers();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Authority = builder.Configuration["Auth:Authority"];
        o.Audience = Audience;
        o.MapInboundClaims = false;   // "sub" stays "sub": it is the patient's id
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,      // a token issued to another client of the realm is refused here
            ValidAudience = Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
        };
    });
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Validate(
        o => !string.IsNullOrWhiteSpace(o.Authority) || o.Configuration is not null
             || o.TokenValidationParameters.IssuerSigningKey is not null || o.TokenValidationParameters.IssuerSigningKeys is not null,
        "Auth:Authority is required: without it no token can be validated.")
    .ValidateOnStart();
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// The clinic dashboard calls this API from the browser, with the token in the Authorization header. Only its
// origins; no credentials (those are for cookies, which this API doesn't use).
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .WithMethods("GET", "POST")
    .WithHeaders("Authorization", "Content-Type")));

// One bucket per patient (per pod). Unauthenticated calls never get this far.
builder.Services.AddRateLimiter(o =>
{
    o.AddPolicy(MeterEndpoints.LinkRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
        http.User.FindFirstValue("sub") is { } sub ? $"patient:{sub}" : $"ip:{http.Connection.RemoteIpAddress}",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = MeterEndpoints.LinkPermitsPerMinute, Window = TimeSpan.FromMinutes(1) }));
    o.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context.HttpContext,
            ProblemDetails = new ProblemDetails { Status = StatusCodes.Status429TooManyRequests, Title = "Too many requests." },
        });
    };
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<UnexpectedExceptionHandler>();
builder.Services.AddHsts(o =>
{
    o.MaxAge = TimeSpan.FromDays(365);
    o.IncludeSubDomains = true;
});

var app = builder.Build();

app.UseApiSecurityHeaders();
app.UseExceptionHandler();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapMeterEndpoints();

app.Run();

static void AddHttpServiceOptions(IServiceCollection services, string section) =>
    services.AddOptions<HttpServiceOptions>(section)
        .BindConfiguration(section)
        .ValidateDataAnnotations()
        .Validate(o => o.BaseAddress is null || o.BaseAddress.IsAbsoluteUri, $"{section}:BaseAddress must be an absolute URL.")
        .ValidateOnStart();

// The trailing '/' is added rather than required: production's vendor address has none (HttpBaseAddress).
static Uri BaseAddressOf(IServiceProvider services, string section) =>
    HttpBaseAddress.WithTrailingSlash(services.GetRequiredService<IOptionsMonitor<HttpServiceOptions>>().Get(section).BaseAddress!);

public partial class Program;
