using HomeReadings.Api;
using HomeReadings.Application;
using HomeReadings.Infrastructure;
using Microsoft.EntityFrameworkCore;
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

builder.Services.AddHttpClient<CareTeamClient>(c => c.BaseAddress = new Uri(builder.Configuration["CareTeam:BaseAddress"]!))
    .AddStandardResilienceHandler();
builder.Services.AddSingleton<ReadingNotifier>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ReadingNotifier>());

builder.Services.AddAuthentication().AddJwtBearer(o =>
{
    o.Authority = builder.Configuration["Auth:Authority"];
    o.Audience = "home-readings";
});
builder.Services.AddAuthorization();
builder.Services.AddHealthChecks().AddDbContextCheck<ReadingsDbContext>();

var app = builder.Build();

app.UseMiddleware<ErrorMiddleware>();
app.UseMiddleware<LanguageMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapReadingEndpoints();
app.MapHealthChecks("/health/live").AllowAnonymous();
app.MapHealthChecks("/health/ready").AllowAnonymous();
app.Run();

public partial class Program;
