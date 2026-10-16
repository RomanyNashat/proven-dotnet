using ClinicReminders.Api;
using ClinicReminders.Application;
using ClinicReminders.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, config) => config.ReadFrom.Configuration(context.Configuration));

// Configuration is read lazily (inside the factories) so overrides applied after this point are honoured.
builder.Services.AddDbContext<RemindersDbContext>((sp, o) => o.UseSqlServer(
    sp.GetRequiredService<IConfiguration>().GetConnectionString("Reminders")
    ?? throw new InvalidOperationException("ConnectionStrings:Reminders is not configured.")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<ReminderService>();
builder.Services.AddScoped<ReminderQueries>();

builder.Services.AddHttpClient<SmsClient>((sp, c) =>
{
    // The trailing slash matters: without it the relative "messages" path replaces the last segment ("v2").
    var baseUrl = sp.GetRequiredService<IConfiguration>()["Sms:BaseUrl"] ?? "https://sms.example.net/api/v2/";
    c.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
    c.Timeout = TimeSpan.FromSeconds(15);
});

// In-memory job store: the schedule is fixed in code, and the job itself claims each reminder atomically
// in the database, so several instances firing at once cannot double-send.
builder.Services.AddQuartz(q =>
{
    var job = new JobKey("send-due-reminders");
    q.AddJob<SendDueRemindersJob>(job);
    // Every morning at 9 Riyadh time, when patients are awake (the host clock is UTC).
    q.AddTrigger(t => t.ForJob(job).WithIdentity("send-due-reminders-daily")
        .WithCronSchedule("0 0 9 * * ?", x => x.InTimeZone(ClinicTime.Riyadh)));
});
builder.Services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IConfiguration>((o, config) =>
    {
        o.Authority = config["Authentication:Jwt:Authority"];
        o.Audience = config["Authentication:Jwt:Audience"];
        o.MapInboundClaims = false;
    });
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
