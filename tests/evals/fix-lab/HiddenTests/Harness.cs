using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using HomeReadings.Application;
using HomeReadings.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace HiddenTests;

/// <summary>What the care team's system received, and how it answers each call.</summary>
public sealed class CareTeamCapture : HttpMessageHandler
{
    private readonly List<(Uri Uri, string Body)> _requests = [];

    /// <summary>(body, how many calls before this one carried the same body) to the status to answer.</summary>
    public Func<string, int, HttpStatusCode> Respond { get; set; } = (_, _) => HttpStatusCode.OK;

    public IReadOnlyList<(Uri Uri, string Body)> Requests
    {
        get { lock (_requests) { return [.. _requests]; } }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        int before;
        lock (_requests)
        {
            before = _requests.Count(r => r.Body == body);
            _requests.Add((request.RequestUri!, body));
        }

        return new HttpResponseMessage(Respond(body, before));
    }
}

/// <summary>The service in-process, in Production, on its own empty PostgreSQL database.</summary>
public sealed class Readings : WebApplicationFactory<Program>
{
    public const string Issuer = "https://id.example.net/realms/patients";
    public const string NationalId = "1012345678";
    public static readonly DateTimeOffset Now = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);
    public static readonly SymmetricSecurityKey Key = new("hidden-tests-signing-key-0123456789abcdef"u8.ToArray());

    private readonly string _database = "eval_" + Guid.NewGuid().ToString("N")[..12];

    public Readings(bool databaseDown = false)
    {
        Pg.Execute(Pg.Master, $"CREATE DATABASE {_database}");
        ConnectionString = databaseDown
            ? "Host=localhost;Port=1;Database=nothing;Username=nobody;Password=x;Timeout=2"
            : new NpgsqlConnectionStringBuilder(Pg.Master) { Database = _database }.ConnectionString;
        PhotoRoot = Path.Combine(Path.GetTempPath(), _database, "volume", "photos");
        Directory.CreateDirectory(PhotoRoot);
        LogFile = Path.Combine(Path.GetTempPath(), _database + ".log");
        Environment.SetEnvironmentVariable("ConnectionStrings__Readings", ConnectionString);
        Environment.SetEnvironmentVariable("Storage__PhotoRoot", PhotoRoot);
        Environment.SetEnvironmentVariable("CareTeam__BaseAddress", "https://careteam.example.net/api/");
        Environment.SetEnvironmentVariable("Auth__Authority", Issuer);
        Environment.SetEnvironmentVariable("Serilog__Using__0", "Serilog.Sinks.File");
        Environment.SetEnvironmentVariable("Serilog__MinimumLevel__Default", "Information");
        Environment.SetEnvironmentVariable("Serilog__WriteTo__0__Name", "File");
        Environment.SetEnvironmentVariable("Serilog__WriteTo__0__Args__path", LogFile);
        Environment.SetEnvironmentVariable("Serilog__WriteTo__0__Args__formatter",
            "Serilog.Formatting.Compact.CompactJsonFormatter, Serilog.Formatting.Compact");
    }

    public string ConnectionString { get; }
    public string PhotoRoot { get; }
    public string LogFile { get; }
    public CareTeamCapture CareTeam { get; } = new();
    public FakeTimeProvider Time { get; } = new(Now);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<TimeProvider>(Time);
            services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => CareTeam));
            services.AddLogging(logging => logging.AddFakeLogging());

            // The tests run the notifier themselves; its loop would race them.
            var fixture = typeof(ReadingNotifier).Assembly;
            foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService)
                         && (d.ImplementationType is null ? d.ImplementationFactory is not null && d.ImplementationFactory.Method.Module.Assembly == fixture
                             : d.ImplementationType.Assembly == fixture)).ToList())
            {
                services.Remove(hosted);
            }

            // The identity provider is ours: same issuer and audience, our key, the service's own other settings.
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                configuration.SigningKeys.Add(Key);
                o.Authority = null;
                o.RequireHttpsMetadata = false;
                o.Configuration = configuration;
                o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                o.TokenValidationParameters.IssuerSigningKey = Key;
                o.TokenValidationParameters.ValidIssuer = Issuer;
            });
        });
    }

    public static string Token(int patient, TimeSpan? expiresIn = null)
    {
        var now = DateTime.UtcNow;
        var expires = now + (expiresIn ?? TimeSpan.FromMinutes(15));
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = "home-readings",
            Subject = new ClaimsIdentity([new Claim("sub", patient.ToString(System.Globalization.CultureInfo.InvariantCulture))]),
            IssuedAt = expires.AddMinutes(-30),
            NotBefore = expires.AddMinutes(-30),
            Expires = expires,
            SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.HmacSha256),
        });
    }

    /// <summary>Starts the service, then creates the tables from its model if it didn't.</summary>
    public HttpClient Start(int? patient = 1, string? language = null)
    {
        var client = CreateClient();
        if (patient is { } id)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(id));
        }

        if (language is not null)
        {
            client.DefaultRequestHeaders.Add("x-language", language);
        }

        if (Pg.Scalar(ConnectionString, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public'") == 0)
        {
            using var scope = Services.CreateScope();
            Pg.Execute(ConnectionString, scope.ServiceProvider.GetRequiredService<ReadingsDbContext>().Database.GenerateCreateScript());
        }

        return client;
    }

    public HttpClient As(int patient, string? language = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(patient));
        if (language is not null)
        {
            client.DefaultRequestHeaders.Add("x-language", language);
        }

        return client;
    }

    public static async Task<int> PostAsync(HttpClient client, object reading)
    {
        var response = await client.PostAsJsonAsync("/readings", reading);
        var body = await response.Content.ReadAsStringAsync();
        Xunit.Assert.True(response.StatusCode == HttpStatusCode.Created, $"POST /readings: {(int)response.StatusCode} {body}");
        return System.Text.Json.JsonDocument.Parse(body).RootElement.GetProperty("id").GetInt32();
    }

    public static object Reading(string value = "6.2", string collectedAt = "2026-11-02T08:00:00Z", string nationalId = NationalId, string unit = "mmol/L") =>
        new { nationalId, value, unit, collectedAt, notes = "before breakfast" };

    /// <summary>One run of the notifier, as its loop would start it. Returns what it threw, if anything.</summary>
    public async Task<Exception?> NotifyAsync()
    {
        using var scope = Services.CreateScope();
        var notifier = scope.ServiceProvider.GetRequiredService<ReadingNotifier>();
        var run = Task.Run(() => notifier.RunOnceAsync(CancellationToken.None));

        // The clock is ours: a retry or a timeout waits on it, so move it on while the run is waiting.
        for (var step = 0; !run.IsCompleted && step < 6000; step++)
        {
            await Task.WhenAny(run, Task.Delay(20));
            if (!run.IsCompleted)
            {
                Time.Advance(TimeSpan.FromSeconds(1));
            }
        }

        try
        {
            await run;
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    /// <summary>Everything the service logged: Serilog's file, and whatever reached the logger factory's providers.</summary>
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
            NpgsqlConnection.ClearAllPools();
            try { Pg.Execute(Pg.Master, $"DROP DATABASE IF EXISTS {_database} WITH (FORCE)"); }
            catch (NpgsqlException) { }
        }
    }
}

public static class Pg
{
    public static string Master => Environment.GetEnvironmentVariable("EVAL_PG")
        ?? "Host=localhost;Username=postgres;Password=postgres;Database=postgres";

    public static void Execute(string connectionString, string sql)
    {
        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }

    public static int Scalar(string connectionString, string sql)
    {
        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
