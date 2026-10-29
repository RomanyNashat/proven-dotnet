using System.Globalization;
using System.Threading.RateLimiting;
using FluentValidation;
using LabResults.Api;
using LabResults.Application;
using LabResults.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Production skips these by default, which hid a captive "scoped" current user in the pooled context.
builder.Host.UseDefaultServiceProvider(o =>
{
    o.ValidateScopes = true;
    o.ValidateOnBuild = true;
});
builder.WebHost.ConfigureKestrel(k => k.AddServerHeader = false);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddLabDb();

builder.Services.AddTransient<IValidator<RecordResultRequest>, RecordResultValidator>();
builder.Services.AddTransient<IValidator<LookupRequest>, LookupRequestValidator>();

// Settings checked at start-up, not at the first request that needs them.
builder.Services.AddOptions<AuthOptions>().BindConfiguration(AuthOptions.Section).ValidateDataAnnotations().ValidateOnStart();
foreach (var section in new[] { DownstreamOptions.Labs, DownstreamOptions.Sms })
{
    builder.Services.AddOptions<DownstreamOptions>(section).BindConfiguration(section).ValidateDataAnnotations().ValidateOnStart();
}

// The national ID is in the lab URL's path, and IHttpClientFactory logs full URIs (it redacts only the query).
builder.Services.AddHttpClient<LabClient>((sp, http) =>
    {
        http.BaseAddress = sp.GetRequiredService<IOptionsMonitor<DownstreamOptions>>().Get(DownstreamOptions.Labs).NormalizedBaseAddress;
        http.Timeout = Timeout.InfiniteTimeSpan;   // the resilience handler owns the timeouts
    })
    .RemoveAllLoggers()
    .AddStandardResilienceHandler(o =>
    {
        o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
        o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(15);
        o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
    });
builder.Services.AddHttpClient<SmsClient>((sp, http) =>
    {
        http.BaseAddress = sp.GetRequiredService<IOptionsMonitor<DownstreamOptions>>().Get(DownstreamOptions.Sms).NormalizedBaseAddress;
        http.Timeout = Timeout.InfiniteTimeSpan;
    })
    .AddStandardResilienceHandler(o =>
    {
        // A timed-out POST may have been sent; retrying it texts the patient twice.
        o.Retry.DisableForUnsafeHttpMethods();
        o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
        o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(20);
        o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
    });
builder.Services.AddTransient<ISmsSender>(sp => sp.GetRequiredService<SmsClient>());
builder.Services.AddScoped<ISmsRelay, SmsOutboxRelay>();
builder.Services.AddHostedService<NotificationWorker>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Audience = AuthOptions.Audience;
        o.RequireHttpsMetadata = true;
        o.MapInboundClaims = false;   // keep "sub" as "sub"
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidAudience = AuthOptions.Audience,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "preferred_username",
            RoleClaimType = "roles",
        };
        o.Events = new JwtBearerEvents
        {
            // Keycloak's roles sit inside JSON claims; RequireRole sees nothing until they're copied out.
            OnTokenValidated = context =>
            {
                if (context.Principal?.Identity is System.Security.Claims.ClaimsIdentity identity)
                {
                    KeycloakRoles.AddRoleClaims(identity, AuthOptions.Audience);
                }

                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<AuthOptions>>((o, auth) => o.Authority = auth.Value.Authority);
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy(ResultEndpoints.ClinicianPolicy, p => p.RequireRole("clinician"));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<UnexpectedExceptionHandler>();
builder.Services.AddHsts(o =>
{
    o.MaxAge = TimeSpan.FromDays(365);
    o.IncludeSubDomains = true;
});

// Behind nginx: trust the proxy networks SRE gives (ForwardedHeaders:KnownNetworks), two hops at most.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.ForwardLimit = 2;
    foreach (var network in builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
    {
        o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
    }
});

// One bucket per clinician, so the lookup can't be used to walk through national IDs.
builder.Services.AddRateLimiter(o =>
{
    o.AddPolicy(ResultEndpoints.LookupRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
        http.User.FindFirst("sub")?.Value is { } sub ? $"user:{sub}" : $"ip:{http.Connection.RemoteIpAddress}",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) }));
    o.OnRejected = async (ctx, ct) =>
    {
        var http = ctx.HttpContext;
        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            http.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        await http.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new Microsoft.AspNetCore.Mvc.ProblemDetails { Status = StatusCodes.Status429TooManyRequests, Title = "Too many requests." },
        });
    };
});

var app = builder.Build();

app.UseApiSecurityHeaders();
app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseHsts();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapResultEndpoints();
app.MapGet("/health/live", () => TypedResults.Ok()).AllowAnonymous();

app.Run();

public partial class Program;
