using HomeReadings.Api;
using HomeReadings.Application;
using HomeReadings.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, config) => config.ReadFrom.Configuration(builder.Configuration));

builder.Services.AddDbContext<ReadingsDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Readings")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentPatient, HttpCurrentPatient>();
builder.Services.AddScoped<ReadingService>();
builder.Services.AddScoped<ReadingQueries>();
builder.Services.AddSingleton<PhotoStore>();

// The care team has no idempotency key: a retried POST is a second alert to a nurse. GETs may retry.
builder.Services.AddHttpClient<CareTeamClient>(c => c.BaseAddress = new Uri(builder.Configuration["CareTeam:BaseAddress"]!))
    .AddStandardResilienceHandler(o => o.Retry.DisableForUnsafeHttpMethods());
builder.Services.AddSingleton<ReadingNotifier>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ReadingNotifier>());

builder.Services.AddAuthentication().AddJwtBearer(o =>
{
    o.Authority = builder.Configuration["Auth:Authority"];
    o.Audience = "home-readings";
    o.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
});
builder.Services.AddAuthorization();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddDbContextCheck<ReadingsDbContext>(tags: ["ready"]);

var app = builder.Build();

app.UseMiddleware<ErrorMiddleware>();
app.UseMiddleware<LanguageMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapReadingEndpoints();
// Liveness says the process answers; a database outage must not get every pod restarted.
app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new() { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();
app.Run();

public partial class Program;
