using System.Security.Claims;
using System.Threading.RateLimiting;
using FluentValidation;
using LabResults.Api;
using LabResults.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Lifetime checks in every environment, not only Development: a scoped service captured by a singleton
// (as the old audit interceptor was, in the pool's options) now stops the start-up.
builder.Host.UseDefaultServiceProvider(o =>
{
    o.ValidateScopes = true;
    o.ValidateOnBuild = true;
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();
builder.Services.AddHsts(o =>
{
    o.MaxAge = TimeSpan.FromDays(365);
    o.IncludeSubDomains = true;
});

builder.Services.AddDbContextPool<LabDbContext>((sp, o) => o
    .UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Lab")));

builder.Services.AddSingleton<IValidator<RecordResultRequest>, RecordResultRequestValidator>();
builder.Services.AddSingleton<IValidator<LookupRequest>, LookupRequestValidator>();

builder.Services.AddOptions<HttpServiceOptions>(HttpServiceOptions.Labs)
    .BindConfiguration(HttpServiceOptions.Labs).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<HttpServiceOptions>(HttpServiceOptions.Sms)
    .BindConfiguration(HttpServiceOptions.Sms).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<AuthOptions>()
    .BindConfiguration(AuthOptions.Section).ValidateDataAnnotations().ValidateOnStart();

builder.Services
    .AddHttpClient<LabClient>((sp, http) =>
    {
        http.BaseAddress = sp.GetRequiredService<IOptionsMonitor<HttpServiceOptions>>().Get(HttpServiceOptions.Labs).BaseAddress;
        http.Timeout = Timeout.InfiniteTimeSpan;   // the resilience handler owns the timeouts
    })
    // The national ID is in the URL path; the default request logging would write it at Information.
    .RemoveAllLoggers()
    .AddStandardResilienceHandler(o =>
    {
        o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
        o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(15);
        o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
    });
builder.Services
    .AddHttpClient<SmsClient>((sp, http) =>
    {
        http.BaseAddress = sp.GetRequiredService<IOptionsMonitor<HttpServiceOptions>>().Get(HttpServiceOptions.Sms).BaseAddress;
        http.Timeout = Timeout.InfiniteTimeSpan;
    })
    .AddStandardResilienceHandler(o =>
    {
        // The gateway takes no idempotency key: a retried POST can send twice. The outbox retries later instead.
        o.Retry.DisableForUnsafeHttpMethods();
        o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
        o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
        o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
    });

builder.Services.AddScoped<SmsOutboxRelay>();
builder.Services.AddHostedService<SmsOutboxWorker>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<AuthOptions>>((o, auth) =>
    {
        o.Authority = auth.Value.Authority;
        o.Audience = AuthOptions.Audience;
        o.RequireHttpsMetadata = true;
        o.MapInboundClaims = false;   // "sub" stays "sub"
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidAudience = AuthOptions.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "preferred_username",
            RoleClaimType = KeycloakRoles.RoleClaim,
        };
        o.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                if (context.Principal?.Identity is ClaimsIdentity identity)
                {
                    KeycloakRoles.AddTo(identity, AuthOptions.Audience);
                }

                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy("clinician", p => p.RequireRole("clinician"));

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // One bucket per clinician: a single token can't walk through national IDs.
    o.AddPolicy(ResultEndpoints.LookupRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
        http.User.FindFirstValue("sub") is { } sub ? $"sub:{sub}" : $"ip:{http.Connection.RemoteIpAddress}",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

// First, and in OnStarting: the exception handler clears headers set on the way in.
app.Use((http, next) =>
{
    http.Response.OnStarting(() =>
    {
        var headers = http.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
        headers["Referrer-Policy"] = "no-referrer";
        if (!headers.ContainsKey("Cache-Control"))
        {
            headers.CacheControl = "no-store";   // patient data must not sit in a shared cache
        }

        return Task.CompletedTask;
    });
    return next(http);
});
app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapResultEndpoints();
app.MapGet("/health/live", () => TypedResults.Ok()).AllowAnonymous();

app.Run();

public partial class Program;
