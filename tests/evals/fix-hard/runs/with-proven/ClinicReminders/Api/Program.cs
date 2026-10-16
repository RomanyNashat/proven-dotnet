using ClinicReminders.Api;
using ClinicReminders.Application;
using ClinicReminders.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quartz;
using Serilog;
using Serilog.Extensions.Logging;

GlobalizationGuard.RequireArabicCulture();

var builder = WebApplication.CreateBuilder(args).ValidateServicesInEveryEnvironment();

// Redaction runs in the logger factory that EnableRedaction installs, so Serilog is a provider under it.
// UseSerilog() would replace that factory: the classified Mobile field would vanish and the redactor never run.
// This is what builder.Logging.AddSerilog(logger) does, with the "Serilog" section read when the provider
// is built rather than here, so configuration added after this line (a test host's) still applies.
builder.Logging.ClearProviders();
builder.Logging.EnableRedaction();
builder.Logging.Services.AddSingleton<ILoggerProvider>(sp => new SerilogLoggerProvider(
    new LoggerConfiguration().ReadFrom.Configuration(sp.GetRequiredService<IConfiguration>()).CreateLogger(),
    dispose: true));
builder.Logging.AddFilter<SerilogLoggerProvider>(null, LogLevel.Trace);   // Serilog's own minimum level decides
builder.Services.AddRedaction(r => r.SetRedactor<Microsoft.Extensions.Compliance.Redaction.ErasingRedactor>(ClinicData.PatientIdentifier));

// The connection string is read when the context is built, so configuration supplied later (tests) applies.
builder.Services.AddDbContext<RemindersDbContext>((sp, options) => options.UseSqlServer(
    sp.GetRequiredService<IConfiguration>().GetConnectionString("Reminders")
    ?? throw new InvalidOperationException("ConnectionStrings:Reminders is not set.")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<ReminderService>();
builder.Services.AddScoped<ReminderQueries>();
builder.Services.AddScoped<ReminderDispatch>();

builder.Services.AddOptions<SmsOptions>()
    .BindConfiguration(SmsOptions.Section)
    .ValidateDataAnnotations()
    .Validate(o => o.BaseAddress is null || o.BaseAddress.AbsolutePath.EndsWith('/'),
        "Sms:BaseAddress must end with '/', or its last path segment is dropped from every call.")
    .ValidateOnStart();
// No resilience handler: the send is a POST the provider can't deduplicate, so it is never retried.
builder.Services.AddHttpClient<SmsClient>((sp, client) =>
    client.BaseAddress = sp.GetRequiredService<IOptions<SmsOptions>>().Value.BaseAddress);

// In-memory store: no Quartz tables to create, and nothing to cluster. Every replica runs the trigger, and the
// per-reminder claim in the job keeps that to one SMS per reminder. A run missed while the service is down
// at 09:00 is not caught up.
builder.Services.AddTransient<SendDueRemindersJob>();
builder.Services.AddQuartz(q =>
{
    q.AddJob<SendDueRemindersJob>(job => job.WithIdentity(SendDueRemindersJob.Key));
    q.AddTrigger(trigger => trigger
        .ForJob(SendDueRemindersJob.Key)
        .WithIdentity("send-due-reminders-trigger")
        // Every morning at 9 Riyadh time (06:00 UTC), when patients are awake. Pods run in UTC.
        .WithCronSchedule("0 0 9 * * ?", cron => cron
            .InTimeZone(RiyadhTime.Zone)
            .WithMisfireHandlingInstructionFireAndProceed()));
});
builder.Services.AddQuartzHostedService(o =>
{
    o.WaitForJobsToComplete = true;
    o.AwaitApplicationStarted = true;
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;   // keep "sub" as "sub"
    options.RequireHttpsMetadata = true;
    options.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
});
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<UnexpectedExceptionHandler>();
builder.Services.AddHsts(o =>
{
    o.MaxAge = TimeSpan.FromDays(365);
    o.IncludeSubDomains = true;
});

var app = builder.Build();

// The schema is not created here: it ships as reviewed scripts (db/), applied by the pipeline.
app.UseApiSecurityHeaders();
app.UseExceptionHandler();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapReminderEndpoints();
app.MapGet("/health", () => TypedResults.Ok()).AllowAnonymous();
app.Run();

public partial class Program;
