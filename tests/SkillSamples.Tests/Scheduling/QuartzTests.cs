using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Dapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Quartz;
using SkillSamples.Postgres;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.Scheduling;

public sealed class SlowReports : IDailyReportBuilder
{
    private int _running;
    public int MaxConcurrent;
    public int Runs;
    public readonly System.Collections.Concurrent.ConcurrentQueue<DateTimeOffset> ScheduledFor = new();

    public async Task BuildAsync(DateTimeOffset scheduledFor, CancellationToken ct)
    {
        ScheduledFor.Enqueue(scheduledFor);
        var now = Interlocked.Increment(ref _running);
        InterlockedMax(ref MaxConcurrent, now);
        await Task.Delay(300, ct);
        Interlocked.Decrement(ref _running);
        Interlocked.Increment(ref Runs);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while (value > (seen = Volatile.Read(ref target)) && Interlocked.CompareExchange(ref target, value, seen) != seen) { }
    }
}

public sealed class AnyAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Role, "jobs-admin")], "Any");
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Any")));
    }
}

[Collection("quartz")]   // Quartz keeps schedulers in a process-wide registry by name: one at a time
public sealed class QuartzTests(PgDatabase db) : IClassFixture<PgDatabase>, IAsyncLifetime
{
    private readonly SlowReports _reports = new();

    // Stands in for the pipeline step that applies Quartz's schema before the service starts.
    public async Task InitializeAsync()
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        if (await connection.ExecuteScalarAsync<bool>("SELECT to_regclass('qrtz_job_details') IS NOT NULL"))
            return;
        await connection.ExecuteAsync(await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Scheduling", "quartz_tables_postgres.sql")));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private IHost Build()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        // Quartz keeps the first logger factory it sees in a static; a disposed host's factory then
        // breaks the next host. One factory that is never disposed avoids it.
        builder.Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        builder.Services.AddScheduling(db.ConnectionString);
        builder.Services.AddSingleton<IDailyReportBuilder>(_reports);
        builder.Services.AddHealthChecks().AddCheck<QuartzHealthCheck>("quartz");
        return builder.Build();
    }

    private static async Task Until(Func<bool> done, string what)
    {
        for (var i = 0; i < 200; i++)
        {
            if (done())
                return;
            await Task.Delay(100);
        }
        throw new TimeoutException($"timed out waiting for: {what}");
    }

    [Fact]
    public void Trigger_TwoAmRiyadh_FiresAtElevenPmUtc()
    {
        using var host = Build();
        var trigger = host.Services.GetRequiredService<IOptions<QuartzOptions>>().Value.Triggers
            .Single(t => t.Key.Name == "daily-report-trigger");

        var now = DateTimeOffset.UtcNow;
        var next = trigger.GetFireTimeAfter(now);

        Assert.NotNull(next);
        Assert.Equal((23, 0), (next!.Value.UtcDateTime.Hour, next.Value.UtcDateTime.Minute));
        Assert.True(next.Value - now <= TimeSpan.FromDays(1));
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_ServiceWasDownWhenTheReportWasDue_ItRunsOnceWhenItComesBack()
    {
        // Given: the service has run before, and every instance was down when the report fell due
        using (var first = Build())
        {
            await first.StartAsync();
            await first.StopAsync();
        }
        var due = DateTimeOffset.UtcNow.AddMinutes(-10);
        await db.ExecuteAsync(
            "UPDATE qrtz_triggers SET next_fire_time = @ticks WHERE trigger_name = 'daily-report-trigger'",
            new { ticks = due.UtcTicks });

        // When: the service starts again
        using var host = Build();
        await host.StartAsync();
        try
        {
            // Then: the missed report runs once, not zero times and not twice
            await Task.Delay(TimeSpan.FromSeconds(8));
            var runs = Volatile.Read(ref _reports.Runs);
            Assert.True(runs == 1, $"runs: {runs}; scheduled for: {string.Join(", ", _reports.ScheduledFor)}; due was {due:O}");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public void Production_NoTzdata_TheTriggerStillFiresAtElevenPmUtc()
    {
        ProductionConditions.Require();
        using var host = Build();
        var trigger = host.Services.GetRequiredService<IOptions<QuartzOptions>>().Value.Triggers
            .Single(t => t.Key.Name == "daily-report-trigger");

        var next = trigger.GetFireTimeAfter(DateTimeOffset.UtcNow);

        Assert.NotNull(next);
        Assert.Equal((23, 0), (next!.Value.UtcDateTime.Hour, next.Value.UtcDateTime.Minute));
    }

    [Theory]
    [InlineData("0 0/30 8-17 * * ?", 17, 30)]   // "8-17" still fires at 17:00 and 17:30
    [InlineData("0 0/30 8-16 * * ?", 16, 30)]   // to stop at 17:00, the hours end at 16
    public void Cron_BusinessHours_LastRunOfTheDay(string expression, int hour, int minute)
    {
        var cron = new CronExpression(expression) { TimeZone = TimeZoneInfo.Utc };
        var day = new DateTimeOffset(2026, 10, 11, 0, 0, 0, TimeSpan.Zero);
        var runs = new List<DateTimeOffset>();
        for (var at = cron.GetNextValidTimeAfter(day); at is { } t && t < day.AddDays(1); at = cron.GetNextValidTimeAfter(t))
            runs.Add(t);

        Assert.Equal(new DateTimeOffset(2026, 10, 11, hour, minute, 0, TimeSpan.Zero), runs[^1]);
    }

    [Fact]
    public async Task PersistentStore_JobRunsFromPostgres_AndARestartKeepsOneJob()
    {
        for (var start = 0; start < 2; start++)   // the second start must not fail on the existing job
        {
            using var host = Build();
            await host.StartAsync();
            await host.StopAsync();
        }

        Assert.Equal(1, await db.ScalarAsync<long>("SELECT count(*) FROM qrtz_job_details WHERE job_name = 'daily-report'"));
        Assert.Equal("CRON", await db.ScalarAsync<string>("SELECT trigger_type FROM qrtz_triggers WHERE trigger_name = 'daily-report-trigger'"));
    }

    [Fact]
    public async Task DisallowConcurrentExecution_RunsOneAtATime()
    {
        using var host = Build();
        await host.StartAsync();
        try
        {
            var scheduler = await host.Services.GetRequiredService<ISchedulerFactory>().GetScheduler();
            for (var i = 0; i < 3; i++)
                await scheduler.TriggerJob(DailyReportJob.Key);

            await Until(() => Volatile.Read(ref _reports.Runs) >= 3, "three runs");
            Assert.Equal(1, _reports.MaxConcurrent);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task HealthCheck_HealthyWhenRunning_UnhealthyInStandby()
    {
        using var host = Build();
        await host.StartAsync();
        try
        {
            var health = host.Services.GetRequiredService<HealthCheckService>();
            // AwaitApplicationStarted: the scheduler starts just after the host does, not inside StartAsync.
            var status = HealthStatus.Unhealthy;
            for (var i = 0; i < 100 && status != HealthStatus.Healthy; i++)
            {
                status = (await health.CheckHealthAsync()).Status;
                if (status != HealthStatus.Healthy)
                    await Task.Delay(100);
            }
            Assert.Equal(HealthStatus.Healthy, status);

            var scheduler = await host.Services.GetRequiredService<ISchedulerFactory>().GetScheduler();
            await scheduler.Standby();
            Assert.Equal(HealthStatus.Unhealthy, (await health.CheckHealthAsync()).Status);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task AdminEndpoints_UnknownJobIs404_KnownJobIsAccepted()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        // Quartz keeps the first logger factory it sees in a static; a disposed host's factory then
        // breaks the next host. One factory that is never disposed avoids it.
        builder.Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        builder.Services.AddScheduling(db.ConnectionString);
        builder.Services.AddSingleton<IDailyReportBuilder>(_reports);
        builder.Services.AddAuthentication("Any").AddScheme<AuthenticationSchemeOptions, AnyAuth>("Any", null);
        builder.Services.AddAuthorization(o => o.AddPolicy("JobsAdmin", p => p.RequireRole("jobs-admin")));
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapJobAdmin("JobsAdmin");
        await app.StartAsync();
        try
        {
            var client = app.GetTestClient();
            Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/admin/jobs/no-such-job/pause", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync("/admin/jobs/daily-report/pause", null)).StatusCode);

            var scheduler = await app.Services.GetRequiredService<ISchedulerFactory>().GetScheduler();
            Assert.Equal(TriggerState.Paused, await scheduler.GetTriggerState(new TriggerKey("daily-report-trigger")));
            Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync("/admin/jobs/daily-report/resume", null)).StatusCode);
            Assert.Equal(TriggerState.Normal, await scheduler.GetTriggerState(new TriggerKey("daily-report-trigger")));
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task Schema_UsesTextAndBytea_SoItNeedsTheDbaException()
    {
        var columns = await db.ScalarAsync<string>("""
            SELECT coalesce(string_agg(table_name || '.' || column_name || ' ' || data_type, ', ' ORDER BY 1), '')
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name LIKE 'qrtz%' AND data_type IN ('text', 'bytea')
            """);
        Assert.Contains("qrtz_job_details.job_data bytea", columns);
        Assert.Contains("qrtz_job_details.job_name text", columns);
    }
}
