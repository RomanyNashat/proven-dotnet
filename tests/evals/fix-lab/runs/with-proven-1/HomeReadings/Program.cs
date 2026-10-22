using FluentValidation;
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

// Production skips these checks by default; a captive DbContext would then pass every test.
builder.Host.UseDefaultServiceProvider(o =>
{
    o.ValidateScopes = true;
    o.ValidateOnBuild = true;
});

builder.Services.AddSerilog((services, config) => config.ReadFrom.Configuration(builder.Configuration).AddPiiMasking());

builder.Services.AddDbContext<ReadingsDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Readings")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentPatient, HttpCurrentPatient>();
builder.Services.AddScoped<ReadingService>();
builder.Services.AddScoped<ReadingQueries>();
builder.Services.AddTransient<IValidator<NewReading>, NewReadingValidator>();

builder.Services.AddOptions<StorageOptions>().BindConfiguration(StorageOptions.Section).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddSingleton<PhotoStore>();

builder.Services.AddOptions<CareTeamOptions>().BindConfiguration(CareTeamOptions.Section).ValidateDataAnnotations()
    .Validate(o => o.BaseAddress is null || o.BaseAddress.IsAbsoluteUri, "CareTeam:BaseAddress must be an absolute URL.")
    .ValidateOnStart();
builder.Services.AddHttpClient<CareTeamClient>((services, client) =>
    {
        client.BaseAddress = services.GetRequiredService<IOptions<CareTeamOptions>>().Value.RequiredBaseAddress();
        client.Timeout = Timeout.InfiniteTimeSpan;   // the resilience handler owns the timeouts
    })
    // The care team has no idempotency key: a POST that timed out may have reached a nurse, so it is never
    // retried here. The notifier sends a failed alert again on its next run.
    .AddStandardResilienceHandler(o => o.Retry.DisableForUnsafeHttpMethods());
builder.Services.AddSingleton<ReadingNotifier>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ReadingNotifier>());

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.Authority = builder.Configuration["Auth:Authority"];
    o.Audience = "home-readings";
    o.MapInboundClaims = false;   // keep "sub" as "sub"
    o.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
});

// Deny by default: every endpoint needs a patient token (a numeric sub) unless it says otherwise.
var patientPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
    .RequireAuthenticatedUser()
    .RequireAssertion(context => HttpCurrentPatient.TryRead(context.User, out _))
    .Build();
builder.Services.AddAuthorizationBuilder().SetDefaultPolicy(patientPolicy).SetFallbackPolicy(patientPolicy);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<UnexpectedExceptionHandler>();
builder.Services.AddHealthChecks().AddDbContextCheck<ReadingsDbContext>(tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseApiSecurityHeaders();
app.UseAuthentication();
app.UseAuthorization();
app.MapReadingEndpoints();

// Live: the process answers, nothing else; a database outage must not restart every pod. Ready: the database.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();
app.Run();

public partial class Program;
