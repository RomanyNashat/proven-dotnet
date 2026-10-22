using FluentValidation;
using HomeReadings.Api;
using HomeReadings.Application;
using HomeReadings.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// The host checks service lifetimes only in Development by default; production (and the tests) run in
// Production, where a singleton holding a scoped service would build fine and be shared by every request.
builder.Host.UseDefaultServiceProvider(o =>
{
    o.ValidateScopes = true;
    o.ValidateOnBuild = true;
});

builder.Services.AddSerilog((services, config) => config.ReadFrom.Configuration(builder.Configuration));

builder.Services.AddDbContext<ReadingsDbContext>((services, o) =>
    o.UseNpgsql(services.GetRequiredService<IConfiguration>().GetConnectionString("Readings")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentPatient, HttpCurrentPatient>();
builder.Services.AddScoped<RequestLanguage>();
builder.Services.AddScoped<IValidator<NewReading>, NewReadingValidator>();
builder.Services.AddScoped<ReadingService>();
builder.Services.AddScoped<ReadingQueries>();
builder.Services.AddOptions<StorageOptions>()
    .BindConfiguration(StorageOptions.Section)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<PhotoStore>();

builder.Services.AddCareTeamClient();
builder.Services.AddSingleton<ReadingNotifier>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ReadingNotifier>());

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IConfiguration>((o, config) =>
    {
        o.Authority = config["Auth:Authority"];
        o.Audience = config["Auth:Audience"] ?? "home-readings";
        o.RequireHttpsMetadata = true;
        o.MapInboundClaims = false;   // keep "sub" as "sub"
        o.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
    });

// Deny by default: every endpoint needs a token whose subject is a patient id, unless it opts out.
var patientPolicy = new AuthorizationPolicyBuilder()
    .RequireAuthenticatedUser()
    .RequireAssertion(context => PatientClaims.TryGetPatientId(context.User, out _))
    .Build();
builder.Services.AddAuthorizationBuilder()
    .SetDefaultPolicy(patientPolicy)
    .SetFallbackPolicy(patientPolicy);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<UnexpectedExceptionHandler>();
builder.Services.AddHealthChecks().AddDbContextCheck<ReadingsDbContext>(tags: ["ready"]);

var app = builder.Build();

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseExceptionHandler();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapReadingEndpoints();
// Liveness checks nothing outside the process: a database outage must not restart every pod.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();
app.Run();

public partial class Program;
