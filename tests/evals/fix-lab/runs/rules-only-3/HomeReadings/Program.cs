using HomeReadings.Api;
using HomeReadings.Application;
using HomeReadings.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Settings are read when services are built, not here, so configuration added later (by the host, or a
// test's WebApplicationFactory) is the configuration the service uses.
builder.Services.AddSerilog((services, config) => config
    .ReadFrom.Configuration(services.GetRequiredService<IConfiguration>())
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services.AddDbContext<ReadingsDbContext>((sp, o) =>
    o.UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Readings")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentPatient, HttpCurrentPatient>();
builder.Services.AddScoped<ReadingService>();
builder.Services.AddScoped<ReadingQueries>();

builder.Services.AddOptions<StorageOptions>()
    .BindConfiguration("Storage")
    .Validate(o => !string.IsNullOrWhiteSpace(o.PhotoRoot), "Storage:PhotoRoot is required.")
    .ValidateOnStart();
builder.Services.AddSingleton<PhotoStore>();

builder.Services.AddOptions<CareTeamOptions>()
    .BindConfiguration("CareTeam")
    .Validate(o => Uri.TryCreate(o.BaseAddress, UriKind.Absolute, out _), "CareTeam:BaseAddress must be an absolute URL.")
    .ValidateOnStart();
builder.Services.AddHttpClient<CareTeamClient>((sp, c) => c.BaseAddress = sp.GetRequiredService<IOptions<CareTeamOptions>>().Value.BaseUri)
    // The care team has no idempotency key: a retried POST is a second alert to a nurse. ReadingNotifier
    // decides when to send again.
    .AddStandardResilienceHandler(o => o.Retry.DisableForUnsafeHttpMethods());
builder.Services.AddSingleton<ReadingNotifier>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ReadingNotifier>());

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IConfiguration>((o, config) =>
    {
        o.Authority = config["Auth:Authority"];
        o.Audience = "home-readings";
        o.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
    });

// Deny by default: every endpoint needs a token whose sub is a patient id, unless it opts out.
var patient = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
    .RequireAuthenticatedUser()
    .RequireAssertion(c => HttpCurrentPatient.TryGetId(c.User, out _))
    .Build();
builder.Services.AddAuthorizationBuilder()
    .SetDefaultPolicy(patient)
    .SetFallbackPolicy(patient);

// Errors are ProblemDetails with no exception text (it can hold SQL or patient data), plus an "error"
// message the app can show in the patient's language.
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = c =>
{
    if (c.ProblemDetails.Status >= StatusCodes.Status500InternalServerError)
    {
        c.ProblemDetails.Detail = null;
        c.ProblemDetails.Extensions["error"] = Messages.SomethingWentWrong(c.HttpContext.Request.Headers["x-language"]);
    }
});

// Live says the process runs; ready says it can reach its database. A database outage must not make
// Kubernetes restart every pod.
builder.Services.AddHealthChecks().AddDbContextCheck<ReadingsDbContext>(tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapReadingEndpoints();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();
app.Run();

public partial class Program;
