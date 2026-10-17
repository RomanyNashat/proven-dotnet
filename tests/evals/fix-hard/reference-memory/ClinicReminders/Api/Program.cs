using ClinicReminders.Api;
using ClinicReminders.Application;
using ClinicReminders.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog as a provider, not the logger factory: the factory is what runs the redactor.
builder.Logging.ClearProviders();
builder.Logging.AddSerilog(new LoggerConfiguration().ReadFrom.Configuration(builder.Configuration).CreateLogger(), dispose: true);
builder.Services.AddRedaction(r => r.SetRedactor<Microsoft.Extensions.Compliance.Redaction.ErasingRedactor>(ClinicData.PatientIdentifier));
builder.Logging.EnableRedaction();

builder.Services.AddDbContext<RemindersDbContext>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("Reminders")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<ReminderService>();
builder.Services.AddScoped<ReminderQueries>();
builder.Services.AddScoped<DueReminders>();

// No HttpClient logging on this client: it logs the handler's exception text, and the provider's
// errors can echo the number.
builder.Services.AddHttpClient<SmsClient>(c => c.BaseAddress = new Uri("https://sms.example.net/api/v2/"))
    .RemoveAllLoggers();

builder.Services.AddQuartz(q =>
{
    // In memory: no Quartz tables, and the per-reminder claim keeps a doubled run harmless. A run missed
    // while the service is down isn't caught up.
    q.UseInMemoryStore();
    var job = new JobKey("send-due-reminders");
    q.AddJob<SendDueRemindersJob>(job);
    // Every morning at 9, when patients are awake.
    q.AddTrigger(t => t.ForJob(job).WithCronSchedule("0 0 9 * * ?", c => c.InTimeZone(ClinicTime.Riyadh)));
});
builder.Services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);

builder.Services.AddAuthentication().AddJwtBearer();
builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapReminderEndpoints();
app.MapGet("/health", () => Results.Ok()).AllowAnonymous();
app.Run();

public partial class Program;
