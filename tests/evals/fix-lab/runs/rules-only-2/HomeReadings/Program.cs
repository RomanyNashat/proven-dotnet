using System.Globalization;
using HomeReadings.Api;
using HomeReadings.Application;
using HomeReadings.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Serilog;

// Numbers and dates the service parses or writes never follow the machine's culture.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, config) => config
    .ReadFrom.Configuration(services.GetRequiredService<IConfiguration>())
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services.AddOptions<CareTeamOptions>().BindConfiguration(CareTeamOptions.Section).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<StorageOptions>().BindConfiguration(StorageOptions.Section).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<AuthOptions>().BindConfiguration(AuthOptions.Section).ValidateDataAnnotations().ValidateOnStart();

builder.Services.AddDbContext<ReadingsDbContext>((services, o) =>
    o.UseNpgsql(services.GetRequiredService<IConfiguration>().GetConnectionString("Readings")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentPatient, HttpCurrentPatient>();
builder.Services.AddScoped<ReadingService>();
builder.Services.AddScoped<ReadingQueries>();
builder.Services.AddSingleton<PhotoStore>();

builder.Services.AddCareTeamClient();
builder.Services.AddSingleton<ReadingNotifier>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ReadingNotifier>());

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<AuthOptions>>((o, auth) =>
    {
        o.Authority = auth.Value.Authority;
        o.Audience = "home-readings";
        o.MapInboundClaims = false;
        o.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
    });

// Deny by default: every endpoint needs a patient token unless it says otherwise.
var patientPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
    .RequireAuthenticatedUser()
    .RequireAssertion(context => HttpCurrentPatient.TryGetPatientId(context.User, out _))
    .Build();
builder.Services.AddAuthorizationBuilder()
    .SetDefaultPolicy(patientPolicy)
    .SetFallbackPolicy(patientPolicy);

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddDbContextCheck<ReadingsDbContext>(tags: ["ready"]);

var app = builder.Build();

// Problem details with no exception text: database and stack details stay in the logs.
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.MapReadingEndpoints();

// Live says the process answers; only ready depends on the database, so a database outage
// takes pods out of the load balancer instead of restarting them all.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();
app.Run();

public partial class Program;
