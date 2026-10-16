using ClinicReminders.Api;
using ClinicReminders.Application;
using ClinicReminders.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.Options;
using Quartz;
using Serilog;
using Serilog.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// Serilog is a provider under the Microsoft logger factory, not UseSerilog: UseSerilog replaces that factory,
// and with it the redaction that keeps patients' mobile numbers out of the logs. Built from the final
// configuration, so the "Serilog" section (and any override of it) is what applies.
builder.Logging.ClearProviders();
builder.Services.AddSingleton<ILoggerProvider>(sp => new SerilogLoggerProvider(
    new LoggerConfiguration().ReadFrom.Configuration(sp.GetRequiredService<IConfiguration>()).CreateLogger(),
    dispose: true));
builder.Services.AddRedaction(r => r.SetRedactor<ErasingRedactor>(ClinicData.PatientIdentifier));
builder.Logging.EnableRedaction();

// The schema is applied as a reviewed script, never by the app (no EnsureCreated/Migrate here).
builder.Services.AddDbContext<RemindersDbContext>((sp, o) =>
    o.UseSqlServer(sp.GetRequiredService<IConfiguration>().GetConnectionString("Reminders")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<ReminderService>();
builder.Services.AddScoped<ReminderQueries>();
builder.Services.AddScoped<DueReminders>();

builder.Services.AddOptions<SmsOptions>()
    .BindConfiguration("Sms")
    .ValidateDataAnnotations()
    .ValidateOnStart();

// No retry or resilience handler on purpose: the provider has no idempotency key, so a retried POST can
// text the patient twice. SendDueRemindersJob decides what is safe to try again.
builder.Services.AddHttpClient<SmsClient>((sp, c) =>
{
    var options = sp.GetRequiredService<IOptions<SmsOptions>>().Value;
    // A base address without a trailing slash drops its last segment: ".../api/v2" + "messages" is ".../api/messages".
    c.BaseAddress = new Uri(options.BaseUrl!.AbsoluteUri.TrimEnd('/') + "/");
    c.Timeout = options.Timeout;
});

builder.Services.AddTransient<SendDueRemindersJob>();
builder.Services.AddQuartz(q =>
{
    // In-memory store: the job keeps its own state in the Reminders table (the claim), so no Quartz tables
    // are needed and a missed or doubled run (restart, several pods) neither loses nor repeats a reminder.
    q.UseInMemoryStore();
    var job = new JobKey("send-due-reminders");
    q.AddJob<SendDueRemindersJob>(job);
    // Every hour from 09:00 to 21:00 Riyadh time, while patients are awake. Hourly rather than once at 9, so a
    // run missed while the service was down or deploying is made up an hour later.
    q.AddTrigger(t => t
        .ForJob(job)
        .WithIdentity("send-due-reminders-daytime")
        .WithCronSchedule("0 0 9-21 * * ?", cron => cron
            .InTimeZone(RiyadhTime.Zone)
            .WithMisfireHandlingInstructionFireAndProceed()));
});
builder.Services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    // Keep "sub" as "sub"; by default the handler renames it to the nameidentifier claim type.
    o.MapInboundClaims = false;
    o.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
});
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapReminderEndpoints();
app.MapGet("/health", () => TypedResults.Ok()).AllowAnonymous();
app.Run();

public partial class Program;
