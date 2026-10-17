using ClinicReminders.Api;
using ClinicReminders.Application;
using ClinicReminders.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quartz;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, config) => config.ReadFrom.Configuration(context.Configuration));

// The schema is applied by reviewed migration scripts, never by the app.
builder.Services.AddDbContext<RemindersDbContext>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("Reminders")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<ReminderService>();
builder.Services.AddScoped<ReminderQueries>();
builder.Services.AddScoped<DueReminders>();

// Quartz builds the job inside its own DI scope per run; registering it lets the scope resolve it too.
builder.Services.AddTransient<SendDueRemindersJob>();

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new ExplicitOffsetConverter()));
builder.Services.AddProblemDetails();

builder.Services.AddOptions<SmsOptions>()
    .BindConfiguration(SmsOptions.Section)
    .Validate(o => o.IsValid(), "Sms:BaseUrl must be an absolute https URL and Sms:Timeout between 0 and 1 minute.")
    .ValidateOnStart();
builder.Services.AddHttpClient<SmsClient>((services, client) =>
{
    var sms = services.GetRequiredService<IOptions<SmsOptions>>().Value;
    client.BaseAddress = sms.BaseAddress;
    client.Timeout = sms.Timeout;
});

builder.Services.AddQuartz(q =>
{
    // In-memory store on purpose: the job's state is the reminders table, so there is nothing for Quartz to
    // persist. A missed or overlapping run is harmless: the next run sends what is pending, and a reminder is
    // claimed in the database before it is sent, so several pods can run the job without double-sending.
    var job = new JobKey("send-due-reminders");
    q.AddJob<SendDueRemindersJob>(job);

    // Every 15 minutes from 09:00 to 20:45 Riyadh time, when patients are awake. The zone is explicit: the
    // scheduler's default is the machine's (UTC in a container), which would mean 12:00 to 23:45 in Riyadh.
    q.AddTrigger(t => t.ForJob(job).WithCronSchedule("0 0/15 9-20 * * ?", c => c.InTimeZone(ClinicTime.Riyadh)));
});
builder.Services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    // Keep "sub" as "sub" rather than mapping it to the long NameIdentifier claim type.
    o.MapInboundClaims = false;
    o.TokenValidationParameters.NameClaimType = "sub";
    o.TokenValidationParameters.RoleClaimType = "role";
    o.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
});

// Deny by default: every endpoint needs a signed-in caller with a numeric subject unless it opts out.
var signedInPatient = new AuthorizationPolicyBuilder()
    .RequireAuthenticatedUser()
    .RequireAssertion(c => HttpCurrentUser.SubjectOf(c.User) is not null)
    .Build();
builder.Services.AddAuthorizationBuilder()
    .SetDefaultPolicy(signedInPatient)
    .SetFallbackPolicy(signedInPatient);

builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.MapReminderEndpoints();

// Liveness: the process is up. It does not touch the database, so a database blip does not restart every pod.
app.MapGet("/health", () => Results.Ok()).AllowAnonymous();

// Readiness: the database is reachable.
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();

app.Run();

public partial class Program;
