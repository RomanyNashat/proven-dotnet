using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using LabResults.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

/// <summary>The lab network and the SMS gateway, as the pods see them.</summary>
public sealed class Outside : HttpMessageHandler
{
    private readonly List<(HttpMethod Method, Uri Uri, string Body)> _requests = [];

    public TimeSpan SmsDelay { get; set; } = TimeSpan.Zero;

    public IReadOnlyList<(HttpMethod Method, Uri Uri, string Body)> Requests
    {
        get { lock (_requests) { return [.. _requests]; } }
    }

    public IReadOnlyList<(string To, string Text)> Sms => [.. Requests
        .Where(r => r.Uri.Host == "sms.example.net" && r.Method == HttpMethod.Post)
        .Select(r => JsonDocument.Parse(r.Body).RootElement)
        .Select(e => (e.GetProperty("to").GetString()!, e.GetProperty("text").GetString()!))];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri!.Host == "sms.example.net")
        {
            var body = await request.Content!.ReadAsStringAsync(ct);
            await Task.Delay(SmsDelay, ct);
            lock (_requests) { _requests.Add((request.Method, request.RequestUri, body)); }   // counted once delivered
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }

        lock (_requests) { _requests.Add((request.Method, request.RequestUri, "")); }
        if (request.RequestUri.Host == "labs.example.net" && request.RequestUri.AbsolutePath.EndsWith("/orders", StringComparison.Ordinal))
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new[] { new { orderId = "LAB-7781", test = "HbA1c", orderedAt = "2026-11-01T09:12:00Z" } }),
            };
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }
}

/// <summary>One pod of the service, in-process, in Production, on PostgreSQL. Pods made with the same
/// database share it, its clock and the outside.</summary>
public sealed class Pod : WebApplicationFactory<Program>
{
    public const string Issuer = "https://id.example.net/realms/clinic";
    public const string Audience = "lab-results";
    public const string NationalId = "1012345678";
    public const string NationalIdArabic = "١٠١٢٣٤٥٦٧٨";
    public const string Phone = "+966500000001";
    public static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
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

        Time = sameDatabaseAs?.Time ?? new FakeTimeProvider(Now);
        Outside = sameDatabaseAs?.Outside ?? new Outside();
        ConnectionString = new NpgsqlConnectionStringBuilder(Pg.Master) { Database = Database }.ConnectionString;
        Environment.SetEnvironmentVariable("ConnectionStrings__Lab", ConnectionString);
        Environment.SetEnvironmentVariable("Labs__BaseAddress", "https://labs.example.net/api/");
        Environment.SetEnvironmentVariable("Sms__BaseAddress", "https://sms.example.net/");
        Environment.SetEnvironmentVariable("Auth__Authority", Issuer);
    }

    public string Database { get; }
    public string ConnectionString { get; }
    public Outside Outside { get; }
    public FakeTimeProvider Time { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<TimeProvider>(Time);
            services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => Outside));
            services.AddLogging(logging => logging.AddFakeLogging());

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

    /// <summary>A token as Keycloak issues it: the roles inside the realm_access JSON claim. With
    /// <paramref name="flatRole"/>, also a plain "realm_access.roles" claim, so the other tests don't depend
    /// on how the service reads Keycloak's roles.</summary>
    public static string Token(string user, string role = "clinician", bool flatRole = true, TimeSpan? expiresIn = null)
    {
        var expires = DateTime.UtcNow + (expiresIn ?? TimeSpan.FromMinutes(15));
        var claims = new Dictionary<string, object>
        {
            ["realm_access"] = new Dictionary<string, object> { ["roles"] = new[] { role, "offline_access" } },
            ["preferred_username"] = user,
        };
        if (flatRole)
        {
            claims["realm_access.roles"] = role;
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Subject = new ClaimsIdentity([new Claim("sub", user)]),
            Claims = claims,
            IssuedAt = expires.AddMinutes(-30),
            NotBefore = expires.AddMinutes(-30),
            Expires = expires,
            SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.HmacSha256),
        });
    }

    /// <summary>Starts the pod, and creates the tables from its model if they aren't there yet.</summary>
    public Pod Start()
    {
        _ = Services;
        if (Pg.Scalar(ConnectionString, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public'") == 0)
        {
            using var scope = Services.CreateScope();
            Pg.Execute(ConnectionString, scope.ServiceProvider.GetRequiredService<LabDbContext>().Database.GenerateCreateScript());
        }

        return this;
    }

    public HttpClient As(string user, string role = "clinician", bool flatRole = true, TimeSpan? expiresIn = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(user, role, flatRole, expiresIn));
        return client;
    }

    public static object Result(string test = "HbA1c", decimal value = 6.1m, string nationalId = NationalId, string? notes = null,
        string phone = Phone) =>
        new { patientId = 7, nationalId, phone, test, value, unit = "%", notes };

    public static async Task<int> RecordAsync(HttpClient client, object result)
    {
        var response = await client.PostAsJsonAsync("/results", result);
        var body = await response.Content.ReadAsStringAsync();
        Xunit.Assert.True(response.StatusCode == HttpStatusCode.Created, $"POST /results: {(int)response.StatusCode} {body}");
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetInt32();
    }

    /// <summary>Every result, following each page's <c>next</c> until there is none.</summary>
    public static async Task<List<JsonElement>> AllPagesAsync(HttpClient client, int limit)
    {
        var all = new List<JsonElement>();
        string? next = null;
        for (var page = 0; page < 50; page++)
        {
            var url = $"/results?limit={limit}" + (next is null ? "" : "&before=" + Uri.EscapeDataString(next));
            var body = await client.GetFromJsonAsync<JsonElement>(url);
            all.AddRange(body.GetProperty("items").EnumerateArray());
            next = body.TryGetProperty("next", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            if (next is null)
            {
                return all;
            }
        }

        throw new InvalidOperationException("more than 50 pages");
    }

    /// <summary>Everything the service logged, message, state and exception, one line per record.</summary>
    public IReadOnlyList<string> Logs() =>
        [.. Services.GetFakeLogCollector().GetSnapshot().Select(r =>
            $"[{r.Level}] {r.Category} {r.Message} | {string.Join("; ", (r.StructuredState ?? []).Select(p => p.Key + "=" + p.Value))} | {r.Exception}")];

    /// <summary>Waits up to five seconds. The clock is ours, so it moves on too: a worker that polls on the
    /// injected TimeProvider gets its ticks.</summary>
    public async Task WaitForSmsAsync(int count)
    {
        for (var i = 0; i < 100 && Outside.Sms.Count < count; i++)
        {
            await Task.Delay(50);
            Time.Advance(TimeSpan.FromSeconds(1));
        }
    }

    /// <summary>The same hundred seconds of our clock, in 200 ms steps. Used where the gateway is slow
    /// (K05): at a second per 50 ms, a 300 ms call reads as six seconds on the injected clock, past a
    /// resilience handler's attempt timeout, and a correct sender times out only in the test.</summary>
    public async Task WaitForSlowSmsAsync(int count)
    {
        for (var i = 0; i < 500 && Outside.Sms.Count < count; i++)
        {
            await Task.Delay(50);
            Time.Advance(TimeSpan.FromMilliseconds(200));
        }
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
