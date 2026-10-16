using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using ClinicReminders.Application;
using ClinicReminders.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Quartz;

[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace HiddenTests;

/// <summary>What the SMS provider received, and how it answers.</summary>
public sealed class SmsCapture : HttpMessageHandler
{
    private readonly List<(Uri Uri, string Body)> _requests = [];

    public Func<string, Exception?> Fail { get; set; } = _ => null;

    public IReadOnlyList<(Uri Uri, string Body)> Requests
    {
        get { lock (_requests) { return [.. _requests]; } }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        lock (_requests) { _requests.Add((request.RequestUri!, body)); }
        if (Fail(body) is { } ex)
        {
            throw ex;
        }

        return new HttpResponseMessage(HttpStatusCode.OK);
    }
}

/// <summary>A crash after the SMS went out: the first write after a send throws, once.</summary>
public sealed class CrashAfterSend(SmsCapture sms) : DbCommandInterceptor
{
    private static readonly Regex Write = new(@"\b(UPDATE|INSERT|DELETE|MERGE)\b", RegexOptions.IgnoreCase);
    public bool Armed { get; set; }
    public bool Fired { get; private set; }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(System.Data.Common.DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        Check(command);
        return base.NonQueryExecutingAsync(command, eventData, result, ct);
    }

    public override ValueTask<InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(System.Data.Common.DbCommand command, CommandEventData eventData, InterceptionResult<System.Data.Common.DbDataReader> result, CancellationToken ct = default)
    {
        Check(command);
        return base.ReaderExecutingAsync(command, eventData, result, ct);
    }

    public override InterceptionResult<int> NonQueryExecuting(System.Data.Common.DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Check(command);
        return base.NonQueryExecuting(command, eventData, result);
    }

    private void Check(System.Data.Common.DbCommand command)
    {
        if (Armed && !Fired && sms.Requests.Count > 0 && Write.IsMatch(command.CommandText))
        {
            Fired = true;
            throw new InvalidOperationException("simulated crash after the SMS was sent");
        }
    }
}

public sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Patient", out var id))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity([new Claim("sub", id!), new Claim(ClaimTypes.NameIdentifier, id!)], Scheme);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
    }
}

/// <summary>The service in-process, in Production, on its own empty SQL Server database.</summary>
public sealed class Clinic : WebApplicationFactory<Program>
{
    public static readonly DateTimeOffset Now = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);
    public const string Mobile = "0551234567";

    private readonly bool _validateScopes;
    private readonly string _database = "eval_" + Guid.NewGuid().ToString("N")[..12];

    public Clinic(bool validateScopes = false)
    {
        _validateScopes = validateScopes;
        Sql.Execute(Sql.Master, $"CREATE DATABASE [{_database}]");
        ConnectionString = new SqlConnectionStringBuilder(Sql.Master) { InitialCatalog = _database }.ConnectionString;
        LogFile = Path.Combine(Path.GetTempPath(), _database + ".log");
        Environment.SetEnvironmentVariable("ConnectionStrings__Reminders", ConnectionString);
        Environment.SetEnvironmentVariable("Serilog__Using__0", "Serilog.Sinks.File");
        Environment.SetEnvironmentVariable("Serilog__MinimumLevel__Default", "Information");
        Environment.SetEnvironmentVariable("Serilog__WriteTo__0__Name", "File");
        Environment.SetEnvironmentVariable("Serilog__WriteTo__0__Args__path", LogFile);
        // One JSON object per event, with every property: a field that vanished is visible as missing.
        Environment.SetEnvironmentVariable("Serilog__WriteTo__0__Args__formatter",
            "Serilog.Formatting.Compact.CompactJsonFormatter, Serilog.Formatting.Compact");
        Crash = new CrashAfterSend(Sms);
    }

    public string ConnectionString { get; }
    public string LogFile { get; }
    public SmsCapture Sms { get; } = new();
    public FakeTimeProvider Time { get; } = new(Now);
    public CrashAfterSend Crash { get; }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        if (_validateScopes)
        {
            builder.UseDefaultServiceProvider(o =>
            {
                o.ValidateScopes = true;
                o.ValidateOnBuild = true;
            });
        }

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<TimeProvider>(Time);
            services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => Sms));
            services.AddLogging(logging => logging.AddFakeLogging());
            services.ConfigureDbContext<RemindersDbContext>(o => o.AddInterceptors(Crash));
            services.AddAuthentication(o =>
                {
                    o.DefaultScheme = TestAuth.Scheme;
                    o.DefaultAuthenticateScheme = TestAuth.Scheme;
                    o.DefaultChallengeScheme = TestAuth.Scheme;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuth>(TestAuth.Scheme, _ => { });

            // The scheduler isn't under test, and its store's tables aren't there; its configuration is.
            foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService)
                         && (d.ImplementationType?.FullName ?? "").StartsWith("Quartz", StringComparison.Ordinal)).ToList())
            {
                services.Remove(hosted);
            }
        });
    }

    /// <summary>Starts the service, then creates the tables from its model if it didn't.</summary>
    public async Task<HttpClient> StartAsync(int patient = 1)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Patient", patient.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (Sql.Scalar(ConnectionString, "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Reminders'") == 0)
        {
            using var scope = Services.CreateScope();
            var script = scope.ServiceProvider.GetRequiredService<RemindersDbContext>().Database.GenerateCreateScript();
            foreach (var batch in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(batch))
                {
                    Sql.Execute(ConnectionString, batch);
                }
            }
        }

        return client;
    }

    public static Task<HttpResponseMessage> ScheduleAsync(HttpClient client, int patientId, DateTimeOffset appointmentAt) =>
        client.PostAsJsonAsync("/reminders", new { patientId, mobile = Mobile, appointmentAt });

    /// <summary>One run of the reminder job, as the scheduler would start it.</summary>
    public async Task<Exception?> RunJobAsync()
    {
        using var scope = Services.CreateScope();
        var job = ActivatorUtilities.CreateInstance<SendDueRemindersJob>(scope.ServiceProvider);
        var context = Mock.Of<IJobExecutionContext>(c => c.CancellationToken == CancellationToken.None);
        try
        {
            await job.Execute(context);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    /// <summary>Waits for at least `count` SMS requests (a fix may send from a background relay).</summary>
    public async Task<IReadOnlyList<(Uri Uri, string Body)>> SmsAsync(int count, TimeSpan? settle = null)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (Sms.Requests.Count < count && DateTime.UtcNow < until)
        {
            await Task.Delay(100);
        }

        await Task.Delay(settle ?? TimeSpan.FromSeconds(1));
        return Sms.Requests;
    }

    /// <summary>
    /// Everything the service logged, one line per event: Serilog's file as compact JSON, and the records the
    /// Microsoft logger factory passed to its providers as "[Level] message | key=value; ... | exception".
    /// </summary>
    public IReadOnlyList<string> Logs()
    {
        var lines = new List<string>();
        if (File.Exists(LogFile))
        {
            using var stream = new FileStream(LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            lines.AddRange(new StreamReader(stream).ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries));
        }

        foreach (var record in Services.GetFakeLogCollector().GetSnapshot())
        {
            var state = string.Join("; ", (record.StructuredState ?? []).Select(p => p.Key + "=" + p.Value));
            lines.Add($"[{record.Level}] {record.Message} | {state} | {record.Exception}".ReplaceLineEndings(" "));
        }

        return lines;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            SqlConnection.ClearAllPools();
            try { Sql.Execute(Sql.Master, $"ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}]"); }
            catch (SqlException) { }
        }
    }
}

public static class Sql
{
    public static string Master => Environment.GetEnvironmentVariable("EVAL_SQL")
        ?? "Server=localhost,1433;User Id=sa;Password=Samples-2026!;TrustServerCertificate=true;Encrypt=false";

    public static void Execute(string connectionString, string sql)
    {
        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using var command = new SqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }

    public static int Scalar(string connectionString, string sql)
    {
        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using var command = new SqlCommand(sql, connection);
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
