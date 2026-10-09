using OrderCleanupWorker;
using Microsoft.EntityFrameworkCore;
using Quartz;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<OrdersDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Orders")));
builder.Services.AddOptions<CleanupOptions>().BindConfiguration("Cleanup").ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddQuartz(q =>
{
    var schedule = builder.Configuration["Cleanup:Schedule"]!;
    q.AddJob<CleanupOrdersJob>(j => j.WithIdentity(nameof(CleanupOrdersJob)));
    q.AddTrigger(t => t.ForJob(nameof(CleanupOrdersJob)).WithCronSchedule(schedule, c => c.InTimeZone(TimeZoneInfo.Utc)));
});
builder.Services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);

await builder.Build().RunAsync();
