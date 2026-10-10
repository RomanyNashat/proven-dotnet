using System.Collections.Concurrent;
using Dapper;
using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using SkillSamples.Postgres;
using Xunit;

namespace SkillSamples.Jobs;

public sealed class Calls
{
    public readonly ConcurrentQueue<string> Log = new();
    public readonly ConcurrentBag<bool> TokensCancellable = [];
}

public sealed class RecordingSender(Calls calls) : IReceiptSender
{
    public Task SendAsync(int orderId, CancellationToken ct)
    {
        calls.TokensCancellable.Add(ct.CanBeCanceled);
        calls.Log.Enqueue($"receipt:{orderId}");
        return Task.CompletedTask;
    }
}

public interface IStep
{
    Task RunAsync(string name, CancellationToken ct);
}

public sealed class RecordingStep(Calls calls) : IStep
{
    public Task RunAsync(string name, CancellationToken ct)
    {
        calls.Log.Enqueue(name);
        return Task.CompletedTask;
    }
}

// The attribute on the class: what the skill used to show. Hangfire never sees it.
public interface IExportOnClass
{
    Task RunAsync(int id, CancellationToken ct);
}

public sealed class ExportOnClass : IExportOnClass
{
    [AutomaticRetry(Attempts = 0)]
    public Task RunAsync(int id, CancellationToken ct) => throw new InvalidOperationException("export failed");
}

// The attribute on the interface: what Hangfire reads.
public interface IExportOnInterface
{
    [AutomaticRetry(Attempts = 0)]
    Task RunAsync(int id, CancellationToken ct);
}

public sealed class ExportOnInterface : IExportOnInterface
{
    public Task RunAsync(int id, CancellationToken ct) => throw new InvalidOperationException("export failed");
}

public sealed class NoReport : IDailyReport
{
    public Task GenerateAsync(CancellationToken ct) => Task.CompletedTask;
}

[Collection("hangfire")]   // Hangfire keeps its storage in a static: one host at a time
public sealed class HangfireTests(PgDatabase db) : IClassFixture<PgDatabase>, IAsyncLifetime
{
    private readonly Calls _calls = new();

    // Stands in for the pipeline step that applies Hangfire's schema before the service starts.
    public async Task InitializeAsync()
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync();
        if (await connection.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT FROM pg_namespace WHERE nspname = 'hangfire')"))
            return;
        PostgreSqlObjectsInstaller.Install(connection, "hangfire");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // servers: null → the sample's servers; otherwise a custom server setup for the test.
    private IHost Build(Action<IServiceCollection>? servers = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddJobStorage(db.ConnectionString);
        if (servers is null)
            builder.Services.AddJobServers();
        else
            servers(builder.Services);
        builder.Services.AddSingleton(_calls);
        builder.Services.AddTransient<IReceiptSender, RecordingSender>();
        builder.Services.AddTransient<IStep, RecordingStep>();
        builder.Services.AddTransient<IExportOnClass, ExportOnClass>();
        builder.Services.AddTransient<IExportOnInterface, ExportOnInterface>();
        builder.Services.AddTransient<IDailyReport, NoReport>();
        builder.Services.AddTransient<OrderJobs>();
        return builder.Build();
    }

    private static string? State(IHost host, string jobId)
    {
        using var connection = host.Services.GetRequiredService<JobStorage>().GetConnection();
        return connection.GetStateData(jobId)?.Name;
    }

    private static async Task Until(Func<bool> done, string what)
    {
        for (var i = 0; i < 300; i++)
        {
            if (done())
                return;
            await Task.Delay(100);
        }
        throw new TimeoutException($"timed out waiting for: {what}");
    }

    [Fact]
    public async Task Enqueue_JobRuns_WithHangfiresCancellableToken()
    {
        using var host = Build();
        await host.StartAsync();
        try
        {
            var jobId = host.Services.GetRequiredService<OrderJobs>().QueueReceipt(42);

            await Until(() => State(host, jobId) == "Succeeded", "the receipt job to succeed");
            Assert.Contains("receipt:42", _calls.Log);
            Assert.NotEmpty(_calls.TokensCancellable);
            Assert.All(_calls.TokensCancellable, cancellable => Assert.True(cancellable));   // not CancellationToken.None at run time
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task AutomaticRetry_OnTheClass_IsIgnored_OnTheInterface_IsApplied()
    {
        using var host = Build();
        await host.StartAsync();
        try
        {
            var jobs = host.Services.GetRequiredService<IBackgroundJobClient>();
            var onClass = jobs.Enqueue<IExportOnClass>(e => e.RunAsync(1, CancellationToken.None));
            var onInterface = jobs.Enqueue<IExportOnInterface>(e => e.RunAsync(2, CancellationToken.None));

            await Until(() => State(host, onInterface) == "Failed", "the interface-attributed job to fail");
            await Until(() => State(host, onClass) == "Scheduled", "the class-attributed job to be retried");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task QueueListOrder_IsNotPriority_OnPostgreSql()
    {
        // What the skill used to show: one server, queues listed most urgent first.
        using var host = Build(services => services.AddHangfireServer(o =>
        {
            o.Queues = ["critical", "default", "low"];
            o.WorkerCount = 1;
        }));
        var jobs = host.Services.GetRequiredService<IBackgroundJobClient>();
        var low = jobs.Enqueue<IStep>("low", s => s.RunAsync("low", CancellationToken.None));
        var critical = jobs.Enqueue<IStep>("critical", s => s.RunAsync("critical", CancellationToken.None));

        await host.StartAsync();
        try
        {
            await Until(() => State(host, low) == "Succeeded" && State(host, critical) == "Succeeded", "both jobs");
            var order = _calls.Log.Where(n => n is "low" or "critical").ToList();
            Assert.True(order.SequenceEqual(new[] { "low", "critical" }), $"ran in this order: {string.Join(", ", order)}");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task CriticalQueue_HasAServerOfItsOwn()
    {
        using var host = Build();
        await host.StartAsync();
        try
        {
            var monitoring = host.Services.GetRequiredService<JobStorage>().GetMonitoringApi();
            await Until(() => monitoring.Servers().Count(s => s.Queues.SequenceEqual(new[] { "critical" })) == 1, "the critical-only server");

            var jobId = host.Services.GetRequiredService<IBackgroundJobClient>()
                .Enqueue<IStep>("critical", s => s.RunAsync("urgent", CancellationToken.None));
            await Until(() => State(host, jobId) == "Succeeded", "the critical job");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task Continuation_WaitsForItsParent_AndNeverRunsIfTheParentFails()
    {
        using var host = Build();
        await host.StartAsync();
        try
        {
            var jobs = host.Services.GetRequiredService<IBackgroundJobClient>();
            var ok = jobs.Enqueue<IStep>(s => s.RunAsync("parent", CancellationToken.None));
            var afterOk = jobs.ContinueJobWith<IStep>(ok, s => s.RunAsync("child-of-ok", CancellationToken.None));
            var bad = jobs.Enqueue<IExportOnInterface>(e => e.RunAsync(3, CancellationToken.None));
            var afterBad = jobs.ContinueJobWith<IStep>(bad, s => s.RunAsync("child-of-failed", CancellationToken.None));

            await Until(() => State(host, afterOk) == "Succeeded", "the continuation of the good parent");
            await Until(() => State(host, bad) == "Failed", "the bad parent to fail");
            await Task.Delay(2000);
            Assert.Equal("Awaiting", State(host, afterBad));
            Assert.DoesNotContain("child-of-failed", _calls.Log);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public void Recurring_TwoAmRiyadh_IsElevenPmUtc()
    {
        using var host = Build();
        RecurringJobs.Register(host.Services.GetRequiredService<IRecurringJobManager>());

        using var connection = host.Services.GetRequiredService<JobStorage>().GetConnection();
        var job = connection.GetRecurringJobs().Single(j => j.Id == "daily-report");

        Assert.Equal("low", job.Job.Queue);   // kept with the job; RecurringJobDto.Queue still says "default"
        Assert.NotNull(job.NextExecution);
        Assert.Equal(23, job.NextExecution!.Value.ToUniversalTime().Hour);
    }

    [Fact]
    public async Task Schema_HasUnboundedTextColumns_SoItNeedsTheDbaException()
    {
        var unbounded = await db.ScalarAsync<long>("""
            SELECT count(*) FROM information_schema.columns
            WHERE table_schema = 'hangfire' AND (data_type = 'text' OR (data_type = 'character varying' AND character_maximum_length IS NULL))
            """);
        var columns = await db.ScalarAsync<string>("""
            SELECT coalesce(string_agg(table_name || '.' || column_name || ' ' || data_type, ', ' ORDER BY table_name, column_name), '')
            FROM information_schema.columns
            WHERE table_schema = 'hangfire' AND (data_type IN ('text', 'jsonb') OR (data_type = 'character varying' AND character_maximum_length IS NULL))
            """);
        Assert.True(unbounded > 0, $"unbounded or json columns: {columns}");
    }
}
