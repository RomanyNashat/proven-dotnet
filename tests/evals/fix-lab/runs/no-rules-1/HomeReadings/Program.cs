using HomeReadings.Api;
using HomeReadings.Application;
using HomeReadings.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, config) => config.ReadFrom.Configuration(builder.Configuration));

builder.Services.AddDbContext<ReadingsDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Readings")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentPatient, HttpCurrentPatient>();
builder.Services.AddScoped<ReadingService>();
builder.Services.AddScoped<ReadingQueries>();
builder.Services.AddSingleton<PhotoStore>();

builder.Services.AddHttpClient<CareTeamClient>(c => c.BaseAddress = new Uri(builder.Configuration["CareTeam:BaseAddress"]!))
    // The care team has no idempotency key: a retried POST would page a nurse twice. The notifier retries instead.
    .AddStandardResilienceHandler(o => o.Retry.DisableForUnsafeHttpMethods());
builder.Services.AddSingleton<ReadingNotifier>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ReadingNotifier>());

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.Authority = builder.Configuration["Auth:Authority"];
    o.Audience = "home-readings";
    o.MapInboundClaims = false;
});
builder.Services.AddAuthorization();
builder.Services.AddHealthChecks().AddDbContextCheck<ReadingsDbContext>(tags: ["ready"]);

var app = builder.Build();

app.UseMiddleware<ErrorMiddleware>();
app.UseMiddleware<LanguageMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapReadingEndpoints();
// Live must not depend on the database, or a database blip restarts healthy instances.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();
app.Run();

public partial class Program;
