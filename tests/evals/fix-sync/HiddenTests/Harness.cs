using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using MeterSync.Application;
using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace HiddenTests;

/// <summary>The vendor's API and the SMS gateway, as one pod sees them: what was asked, and the answers.</summary>
public sealed partial class Outside : HttpMessageHandler
{
    private readonly List<(HttpMethod Method, Uri Uri, string Body)> _requests = [];

    /// <summary>Each meter's readings, by serial number, as the vendor returns them.</summary>
    public Dictionary<string, object[]> Meters { get; } = [];

    /// <summary>How long the gateway takes to accept a message.</summary>
    public TimeSpan SmsDelay { get; set; } = TimeSpan.Zero;

    public IReadOnlyList<(HttpMethod Method, Uri Uri, string Body)> Requests
    {
        get { lock (_requests) { return [.. _requests]; } }
    }

    /// <summary>Every SMS sent: (to, text).</summary>
    public IReadOnlyList<(string To, string Text)> Sms => [.. Requests
        .Where(r => r.Uri.Host == "sms.example.net" && r.Method == HttpMethod.Post)
        .Select(r => JsonDocument.Parse(r.Body).RootElement)
        .Select(e => (e.GetProperty("to").GetString()!, e.GetProperty("text").GetString()!))];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        lock (_requests)
        {
            _requests.Add((request.Method, request.RequestUri!, body));
        }

        if (request.RequestUri!.Host == "sms.example.net")
        {
            await Task.Delay(SmsDelay, ct);
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }

        // Any path ending in /meters/{serial}/readings: the path itself is checked by its own test only.
        var match = ReadingsPath().Match(request.RequestUri.AbsolutePath);
        if (request.RequestUri.Host == "meters.example.net" && match.Success
            && Meters.TryGetValue(Uri.UnescapeDataString(match.Groups[1].Value), out var readings))
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(readings) };
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    [GeneratedRegex("/meters/([^/]+)/readings$")]
    private static partial Regex ReadingsPath();
}

/// <summary>One pod of the service, in-process, in Production, on PostgreSQL. Pods made with the same
/// database share it, as pods do.</summary>
public sealed class Pod : WebApplicationFactory<Program>
{
    public const string Issuer = "https://id.example.net/realms/patients";
    public const string Audience = "meter-sync";
    public const string ClinicOrigin = "https://clinic.example.net";
    // The meter is linked at 08:00; the readings are taken after that, and the sync runs at 12:00.
    public static readonly DateTimeOffset LinkedAt = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    public static readonly SymmetricSecurityKey Key = new("hidden-tests-signing-key-0123456789abcdef"u8.ToArray());

    private readonly bool _ownsDatabase;

    public Pod(Pod? sameDatabaseAs = null)
    {
        _ownsDatabase = sameDatabaseAs is null;
        Database = sameDatabaseAs?.Database ?? "eval_" + Guid.NewGuid().ToString("N")[..12];
        if (_ownsDatabase)
        {
            Pg.Execute(Pg.Master, $"CREATE DATABASE {Database}");
        }

        Time = sameDatabaseAs?.Time ?? new FakeTimeProvider(LinkedAt);   // pods share one clock
        ConnectionString = new NpgsqlConnectionStringBuilder(Pg.Master) { Database = Database }.ConnectionString;
        Environment.SetEnvironmentVariable("ConnectionStrings__Sync", ConnectionString);
        Environment.SetEnvironmentVariable("MeterVendor__BaseAddress", "https://meters.example.net/v2");
        Environment.SetEnvironmentVariable("Sms__BaseAddress", "https://sms.example.net/");
        Environment.SetEnvironmentVariable("Auth__Authority", Issuer);
        Environment.SetEnvironmentVariable("Cors__Origins__0", ClinicOrigin);
    }

    public string Database { get; }
    public string ConnectionString { get; }
    public Outside Outside { get; } = new();
    public FakeTimeProvider Time { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<TimeProvider>(Time);
            services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => Outside));

            // The tests run the sync and the sweep themselves; the worker's loop would race them.
            var fixture = typeof(AlertSweeper).Assembly;
            foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService)
                         && (d.ImplementationType is null ? d.ImplementationFactory is not null && d.ImplementationFactory.Method.Module.Assembly == fixture
                             : d.ImplementationType.Assembly == fixture)).ToList())
            {
                services.Remove(hosted);
            }

            // The identity provider is ours: same issuer, our key, the service's own other settings.
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

    public static string Token(int patient, string audience = Audience) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Subject = new ClaimsIdentity([new Claim("sub", patient.ToString(CultureInfo.InvariantCulture))]),
            IssuedAt = DateTime.UtcNow.AddMinutes(-1),
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddMinutes(15),
            SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.HmacSha256),
        });

    /// <summary>Starts the pod, and creates the tables from its model if they aren't there yet.</summary>
    public Pod Start()
    {
        _ = Services;
        if (Pg.Scalar(ConnectionString, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public'") == 0)
        {
            using var scope = Services.CreateScope();
            Pg.Execute(ConnectionString, scope.ServiceProvider.GetRequiredService<SyncDbContext>().Database.GenerateCreateScript());
        }

        return this;
    }

    public HttpClient As(int patient, string audience = Audience)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(patient, audience));
        return client;
    }

    public async Task<int> AddPatientAsync(string nationalId, string phone)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
        var patient = new Patient(nationalId, phone);
        db.Patients.Add(patient);
        await db.SaveChangesAsync();
        return patient.Id;
    }

    /// <summary>A patient with a meter linked through the API, and the vendor holding these readings for it.</summary>
    public async Task<int> PatientWithMeterAsync(string serial, params object[] readings)
    {
        var id = await AddPatientAsync(Patients.NationalId1, Patients.Phone1);
        var response = await As(id).PostAsJsonAsync("/meters", new { nationalId = Patients.NationalId1, serialNumber = serial });
        Xunit.Assert.True(response.StatusCode == HttpStatusCode.Created,
            $"POST /meters: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        Outside.Meters[serial] = readings;
        Time.SetUtcNow(Now);
        return id;
    }

    public static object Reading(string id, string value, string unit = "mg/dL", string takenAt = "2026-11-02T10:00:00Z") =>
        new { id, value, unit, takenAt };

    public async Task SyncAsync()
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MeterSyncService>().SyncOnceAsync(CancellationToken.None);
    }

    public async Task SweepAsync()
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AlertSweeper>().RunOnceAsync(CancellationToken.None);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && _ownsDatabase)
        {
            NpgsqlConnection.ClearAllPools();
            try { Pg.Execute(Pg.Master, $"DROP DATABASE IF EXISTS {Database} WITH (FORCE)"); }
            catch (NpgsqlException) { }
        }
    }
}

public static class Patients
{
    public const string NationalId1 = "1012345678";
    public const string Phone1 = "+966500000001";
    public const string NationalId2 = "1098765432";
    public const string Phone2 = "+966500000002";
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
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }
}
