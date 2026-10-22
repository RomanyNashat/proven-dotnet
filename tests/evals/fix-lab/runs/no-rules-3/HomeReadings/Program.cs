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

// The care team has no idempotency key, so a retried POST would page a nurse twice: no retries for POST.
// A failed or timed-out send stays unsent and is picked up on the next run.
builder.Services.AddHttpClient<CareTeamClient>(c =>
        c.BaseAddress = new Uri(builder.Configuration["CareTeam:BaseAddress"]!.TrimEnd('/') + "/"))
    .AddStandardResilienceHandler(o => o.Retry.DisableForUnsafeHttpMethods());
builder.Services.AddSingleton<ReadingNotifier>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ReadingNotifier>());

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.Authority = builder.Configuration["Auth:Authority"];
    o.Audience = "home-readings";
});
builder.Services.AddAuthorization();
builder.Services.AddHealthChecks().AddDbContextCheck<ReadingsDbContext>(tags: ["ready"]);

var app = builder.Build();

app.UseMiddleware<ErrorMiddleware>();
app.UseMiddleware<LanguageMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapReadingEndpoints();
// Live must not depend on the database, or a database outage restarts every instance.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();
app.Run();

public partial class Program;
