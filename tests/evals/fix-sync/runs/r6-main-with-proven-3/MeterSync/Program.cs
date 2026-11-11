using MeterSync.Api;
using MeterSync.Application;
using MeterSync.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

const string Audience = "meter-sync";

var builder = WebApplication.CreateBuilder(args);

// Scope validation is on in every environment: a scoped DbContext captured by a singleton
// is a bug in Production too, not only in Development.
builder.Host.UseDefaultServiceProvider(o =>
{
    o.ValidateScopes = true;
    o.ValidateOnBuild = true;
});

builder.Services.AddProblemDetails();
builder.Services.AddDbContext<SyncDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Sync")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<PatientDirectory>();
builder.Services.AddScoped<MeterSyncService>();
builder.Services.AddScoped<AlertSweeper>();
builder.Services.AddHostedService<SyncWorker>();

// The configured value has no trailing slash; BaseAddress adds it so "/v2" stays in every URL.
builder.Services.AddHttpClient<MeterVendorClient>(http =>
        http.BaseAddress = BaseAddress.WithTrailingSlash(builder.Configuration["MeterVendor:BaseAddress"]))
    .AddStandardResilienceHandler();

// No retry handler: every POST to the gateway sends a text and it has no idempotency key, so a
// retry after a timeout can text the patient twice. A failed send is retried by the next sweep.
builder.Services.AddHttpClient<SmsClient>(http =>
{
    http.BaseAddress = BaseAddress.WithTrailingSlash(builder.Configuration["Sms:BaseAddress"]);
    http.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Authority = builder.Configuration["Auth:Authority"];
        o.Audience = Audience;

        // Keep "sub" as "sub" instead of mapping it to ClaimTypes.NameIdentifier.
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = true,
            ValidAudience = Audience,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// The clinic dashboard calls this API from the browser with a bearer token (no cookies),
// so only its origin is allowed and credentials are not.
var corsOrigins = CorsOrigins(builder.Configuration);
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(corsOrigins)
    .WithMethods("GET", "POST")
    .WithHeaders("Authorization", "Content-Type")));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseHsts();

// No UseHttpsRedirection: TLS ends at the ingress and the pod is reached over plain http, so a
// redirect would only loop or break health probes. HSTS is still sent on requests that arrive as https.
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

// Accepts both a list ("Cors:Origins:0", ...) and a single comma-separated value ("Cors:Origins").
static string[] CorsOrigins(IConfiguration configuration)
{
    var section = configuration.GetSection("Cors:Origins");
    var single = section.Value?.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
    string[] origins = [.. section.GetChildren().Select(c => c.Value?.Trim()).OfType<string>().Where(v => v.Length > 0), .. single];
    return origins.Length > 0
        ? origins
        : throw new InvalidOperationException("Cors:Origins must list at least one origin.");
}

/// <summary>Entry point; public so integration tests can host it with <c>WebApplicationFactory</c>.</summary>
public partial class Program;
