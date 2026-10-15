using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SkillSamples.Localization;
using SkillSamples.Postgres;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.CronJobs;

public sealed class CronJobTests(PgDatabase db) : IClassFixture<PgDatabase>, IAsyncLifetime
{
    // 02:00 on 16 October in Riyadh: the CronJob's schedule with timeZone Asia/Riyadh.
    private static readonly DateTimeOffset TwoAmRiyadh = new(2026, 10, 15, 23, 0, 0, TimeSpan.Zero);

    private CronDbContext Context() => new(new DbContextOptionsBuilder<CronDbContext>().UseNpgsql(db.ConnectionString).Options);

    // Stands in for the reviewed script the pipeline applies.
    public async Task InitializeAsync()
    {
        if (await db.ScalarAsync<bool>("SELECT to_regclass('cron_orders') IS NOT NULL"))
        {
            return;
        }

        await using var context = Context();
        await context.Database.ExecuteSqlRawAsync(context.Database.GenerateCreateScript());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task ResetAsync()
    {
        await using var context = Context();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE cron_orders, cron_data_migrations, cron_audit_log RESTART IDENTITY");
    }

    private async Task<int> RunAsync<TJob>(TimeProvider time) where TJob : class, IHostedService
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddDbContext<CronDbContext>(o => o.UseNpgsql(db.ConnectionString));
        builder.Services.AddSingleton<JobOutcome>();
        builder.Services.AddSingleton(time);
        builder.Services.AddHostedService<TJob>();
        using var host = builder.Build();
        var outcome = host.Services.GetRequiredService<JobOutcome>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await host.RunAsync(timeout.Token);
        Assert.False(timeout.IsCancellationRequested, "the job didn't stop the host");
        return outcome.ExitCode;
    }

    [Fact]
    public void Yesterday_RunAtTwoAmRiyadh_IsTheRiyadhDayBefore_NotTheUtcOne()
    {
        var time = new FakeTimeProvider(TwoAmRiyadh);

        Assert.Equal(new DateOnly(2026, 10, 15), ReportDay.Yesterday(time, RiyadhTime.Zone));
        Assert.Equal(new DateOnly(2026, 10, 14), DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime.AddDays(-1)));   // the UTC way: a day off
    }

    [Fact]
    public async Task DailyReport_ServiceSucceeds_ReportsRiyadhsYesterday_AndExitsZero()
    {
        var reports = new Mock<IReportService>();
        reports.Setup(r => r.GenerateDailyReportAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync(12);

        var exitCode = await DailyReportHost.RunAsync([], b =>
        {
            b.Logging.ClearProviders();
            b.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(TwoAmRiyadh));
            b.Services.AddScoped(_ => reports.Object);
        });

        Assert.Equal(0, exitCode);
        reports.Verify(r => r.GenerateDailyReportAsync(new DateOnly(2026, 10, 15), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_ReportServiceDown_TheRunExitsWithOne_SoKubernetesRetriesIt()
    {
        // Given the report's database is down when the 02:00 run starts
        var reports = new Mock<IReportService>();
        reports.Setup(r => r.GenerateDailyReportAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("database unreachable"));

        // When the job runs
        var exitCode = await DailyReportHost.RunAsync([], b =>
        {
            b.Logging.ClearProviders();
            b.Services.AddScoped(_ => reports.Object);
        });

        // Then the process exits non-zero: backoffLimit retries it, instead of a green run with no report
        Assert.Equal(1, exitCode);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_OneLegacyValueNobodyMapped_TheMigrationStillFinishes()
    {
        // Given legacy orders, one with a status nobody mapped
        await ResetAsync();
        await using (var context = Context())
        {
            context.Orders.AddRange(
                new LegacyOrder { LegacyStatus = "NEW" },
                new LegacyOrder { LegacyStatus = "ON_HOLD" },
                new LegacyOrder { LegacyStatus = "SHIPPED" });
            await context.SaveChangesAsync();
        }

        // When the migration job runs
        var exitCode = await RunAsync<OrderStatusMigrationJob>(new FakeTimeProvider(TwoAmRiyadh));

        // Then it finishes: two migrated, one left Unknown and counted
        Assert.Equal(0, exitCode);
        await using var after = Context();
        var run = await after.DataMigrations.SingleAsync();
        Assert.Equal((2, 1), (run.RecordsMigrated, run.RecordsUnmapped));
        // A loop that reads "rows still Unknown" until none are left would get this row back on every pass.
        Assert.Equal(1, await after.Orders.CountAsync(o => o.LegacyStatus != null && o.Status == OrderStatus.Unknown));
    }

    [Fact]
    public async Task Migration_RunTwice_TheSecondRunChangesNothing()
    {
        await ResetAsync();
        await using (var context = Context())
        {
            context.Orders.Add(new LegacyOrder { LegacyStatus = "DONE" });
            await context.SaveChangesAsync();
        }

        Assert.Equal(0, await RunAsync<OrderStatusMigrationJob>(new FakeTimeProvider(TwoAmRiyadh)));
        Assert.Equal(0, await RunAsync<OrderStatusMigrationJob>(new FakeTimeProvider(TwoAmRiyadh.AddDays(1))));

        await using var after = Context();
        var run = await after.DataMigrations.SingleAsync();
        Assert.Equal(TwoAmRiyadh, run.CompletedAt);
    }

    [Fact]
    public async Task Retention_DeletesOnlyWhatIsPastIt_InBatches()
    {
        await ResetAsync();
        await using (var context = Context())
        {
            var old = TwoAmRiyadh - AuditRetentionJob.Retention - TimeSpan.FromDays(1);
            context.AuditLog.AddRange(Enumerable.Range(0, 12_000).Select(_ => new AuditEntry { CreatedAt = old }));
            context.AuditLog.Add(new AuditEntry { CreatedAt = TwoAmRiyadh - TimeSpan.FromDays(10) });
            await context.SaveChangesAsync();
        }

        Assert.Equal(0, await RunAsync<AuditRetentionJob>(new FakeTimeProvider(TwoAmRiyadh)));

        await using var after = Context();
        Assert.Equal(1, await after.AuditLog.CountAsync());
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public void Production_NoTzdata_TheReportDayIsStillRiyadhs()
    {
        ProductionConditions.Require();

        Assert.Equal(new DateOnly(2026, 10, 15), ReportDay.Yesterday(new FakeTimeProvider(TwoAmRiyadh), RiyadhTime.Zone));
    }
}
