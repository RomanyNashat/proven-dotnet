using ClinicReminders.Api;
using ClinicReminders.Application;
using ClinicReminders.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, config) => config.ReadFrom.Configuration(context.Configuration));
builder.Services.AddRedaction(r => r.SetRedactor<Microsoft.Extensions.Compliance.Redaction.ErasingRedactor>(ClinicData.PatientIdentifier));
builder.Logging.EnableRedaction();

builder.Services.AddDbContext<RemindersDbContext>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("Reminders")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<ReminderService>();
builder.Services.AddScoped<ReminderQueries>();
builder.Services.AddScoped<DueReminders>();

builder.Services.AddHttpClient<SmsClient>(c =>
{
    // Trailing slash matters: without it "messages" replaces the last path segment (v2).
    c.BaseAddress = new Uri(builder.Configuration["Sms:BaseUrl"] ?? "https://sms.example.net/api/v2/");
    c.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddQuartz(q =>
{
    // In-memory store: the job and trigger are defined in code, and the QRTZ_ tables are not created by EnsureCreated.
    var job = new JobKey("send-due-reminders");
    q.AddJob<SendDueRemindersJob>(job);
    // Every morning at 9, when patients are awake.
    q.AddTrigger(t => t.ForJob(job).WithCronSchedule("0 0 9 * * ?", c => c.InTimeZone(ReminderService.Riyadh)));
});
builder.Services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);

builder.Services.AddAuthentication().AddJwtBearer();
builder.Services.AddAuthorization();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<RemindersDbContext>().Database.EnsureCreatedAsync();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapReminderEndpoints();
app.MapGet("/health", () => Results.Ok()).AllowAnonymous();
app.Run();

public partial class Program;
