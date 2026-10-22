using HomeReadings.Api;
using HomeReadings.Application;
using HomeReadings.Infrastructure;
using HomeReadings.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, config) => config
    .ReadFrom.Configuration(services.GetRequiredService<IConfiguration>())
    .Enrich.FromLogContext());

builder.Services.AddOptions<CareTeamOptions>().BindConfiguration(CareTeamOptions.Section).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<StorageOptions>().BindConfiguration(StorageOptions.Section).ValidateDataAnnotations().ValidateOnStart();

builder.Services.AddDbContext<ReadingsDbContext>((sp, o) => o.UseNpgsql(
    sp.GetRequiredService<IConfiguration>().GetConnectionString("Readings")
        ?? throw new InvalidOperationException("ConnectionStrings:Readings is not set.")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentPatient, HttpCurrentPatient>();
builder.Services.AddScoped<ReadingService>();
builder.Services.AddScoped<ReadingQueries>();
builder.Services.AddSingleton<PhotoStore>();

builder.Services.AddHttpClient<CareTeamClient>((sp, c) =>
    {
        // A base address without the trailing slash would drop its last segment ("api") from "alerts".
        var address = sp.GetRequiredService<IOptions<CareTeamOptions>>().Value.BaseAddress!.ToString();
        c.BaseAddress = new Uri(address.EndsWith('/') ? address : address + "/");
    })
    .AddStandardResilienceHandler(o =>
    {
        // The care team's system has no idempotency key: a retried POST (after a timeout or a 5xx it
        // may already have acted on) is a second page to a nurse. The notifier retries on its next run.
        o.Retry.DisableForUnsafeHttpMethods();
    });
builder.Services.AddSingleton<ReadingNotifier>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ReadingNotifier>());

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.Authority = builder.Configuration["Auth:Authority"];
    o.Audience = "home-readings";
    o.MapInboundClaims = false;
    o.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
});
builder.Services.AddAuthorization(o =>
{
    // Deny by default, and only for a token whose sub is a patient id.
    o.DefaultPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .RequireAssertion(context => PatientClaims.TryGetPatientId(context.User, out _))
        .Build();
    o.FallbackPolicy = o.DefaultPolicy;
});
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddDbContextCheck<ReadingsDbContext>(tags: ["ready"]);

var app = builder.Build();

// Problem details with no exception text: messages from Npgsql and EF name tables, columns and values.
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.MapReadingEndpoints();

// Liveness never touches the database: a database outage must not make Kubernetes restart every pod.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();
app.Run();

public partial class Program;
