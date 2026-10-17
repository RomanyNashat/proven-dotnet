using ClinicReminders.Api;
using ClinicReminders.Api.Logging;
using ClinicReminders.Application;
using ClinicReminders.Infrastructure;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

// Lifetimes are checked in every environment: a singleton holding a scoped DbContext stops the start-up.
var builder = WebApplication.CreateBuilder(args).ValidateServicesInEveryEnvironment();

builder.AddClassifiedSerilog();

builder.Services.AddDbContext<RemindersDbContext>((sp, o) =>
    o.UseSqlServer(sp.GetRequiredService<IConfiguration>().GetConnectionString("Reminders")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<ReminderService>();
builder.Services.AddScoped<ReminderQueries>();
builder.Services.AddScoped<IDueReminders, DueReminderStore>();
builder.Services.AddScoped<ReminderDispatcher>();
builder.Services.AddTransient<IValidator<ScheduleReminder>, ScheduleReminderValidator>();
builder.Services.AddSmsClient();
builder.Services.AddReminderScheduling();

// Issuer, audience and keys come from Authentication:Schemes:Bearer; this only tightens what that leaves open.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;   // keep "sub" as "sub"
    o.RequireHttpsMetadata = true;
    o.TokenValidationParameters.ValidateIssuer = true;
    o.TokenValidationParameters.ValidateAudience = true;
    o.TokenValidationParameters.ValidateLifetime = true;
    o.TokenValidationParameters.ValidateIssuerSigningKey = true;
    o.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
    o.TokenValidationParameters.NameClaimType = "sub";
});
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());   // deny by default

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<UnexpectedExceptionHandler>();
builder.Services.AddHsts(o =>
{
    o.MaxAge = TimeSpan.FromDays(365);
    o.IncludeSubDomains = true;
});
builder.Services.AddReminderRateLimits(builder.Configuration);

var app = builder.Build();

// Quartz keeps a logger factory in a static, set by the first host in the process. Point it at this host's
// (the redacting one), so Quartz's own lines are redacted too, and a second host in one process (tests)
// doesn't log through a disposed factory.
Quartz.Logging.LogContext.SetCurrentLogProvider(app.Services.GetRequiredService<ILoggerFactory>());

GlobalizationGuard.RequireArabicCulture();   // no ICU: fail the deploy, not the first query or SMS

// The schema is applied from the reviewed scripts in db/, never by the app.
app.UseApiSecurityHeaders();
app.UseExceptionHandler();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapReminderEndpoints();
app.MapGet("/health", () => Results.Ok()).AllowAnonymous();
app.Run();

public partial class Program;
