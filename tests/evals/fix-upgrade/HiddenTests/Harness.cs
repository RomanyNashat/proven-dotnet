using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using ClinicQueue.Application;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace HiddenTests;

/// <summary>The service in-process, in Production, on its own PostgreSQL database, behind nginx in the pod network.</summary>
public sealed class Pod : WebApplicationFactory<Program>
{
    public const string Issuer = "https://id.example.net/realms/staff";
    public static readonly IPAddress NginxPod = IPAddress.Parse("10.42.0.7");
    public static readonly SymmetricSecurityKey Key = new("hidden-tests-signing-key-0123456789abcdef"u8.ToArray());

    private readonly IClinicSource? _source;

    public Pod(IClinicSource? source = null)
    {
        _source = source;
        Database = "upg_" + Guid.NewGuid().ToString("N")[..12];
        Pg.Execute(Pg.Master, $"CREATE DATABASE {Database}");
        ConnectionString = new NpgsqlConnectionStringBuilder(Pg.Master) { Database = Database }.ConnectionString;
        Pg.Execute(ConnectionString, Pg.Schema);
        Pg.Execute(ConnectionString, "INSERT INTO clinic (name) VALUES ('Cardiology'), ('Dermatology')");
        Environment.SetEnvironmentVariable("ConnectionStrings__Queue", ConnectionString);
        Environment.SetEnvironmentVariable("Auth__Authority", Issuer);
        Environment.SetEnvironmentVariable("Proxies__Network", "10.42.0.0/16");
    }

    public string Database { get; }
    public string ConnectionString { get; }
    public FakeLogCollector Logs => Services.GetRequiredService<FakeLogCollector>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureLogging(l => l.AddFakeLogging());
        builder.ConfigureServices(services =>
        {
            // Every request reaches the app from nginx's pod, as it does in the cluster.
            services.AddTransient<Microsoft.AspNetCore.Hosting.IStartupFilter, FromNginx>();
            if (_source is not null)
            {
                services.RemoveAll<IClinicSource>();
                services.AddSingleton(_source);
            }

            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                configuration.SigningKeys.Add(Key);
                o.RequireHttpsMetadata = false;
                o.Configuration = configuration;
                o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                o.TokenValidationParameters.IssuerSigningKey = Key;
                o.TokenValidationParameters.ValidIssuer = Issuer;
            });
        });
    }

    public HttpClient As(int patient, string audience = "clinic-queue")
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(patient, audience));
        return client;
    }

    public static string Token(int patient, string audience) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Claims = new Dictionary<string, object> { ["sub"] = patient.ToString() },
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.HmacSha256),
        });

    public async Task<int> BookAsync(HttpClient client, int clinic, string day)
    {
        using var response = await client.PostAsJsonAsync("/visits", new { clinicId = clinic, day });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Booked>())!.Id;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        Pg.Execute(Pg.Master, $"DROP DATABASE IF EXISTS {Database} WITH (FORCE)");
    }

    public sealed record Booked(int Id, int Position);

    private sealed class FromNginx : Microsoft.AspNetCore.Hosting.IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((http, after) =>
            {
                http.Connection.RemoteIpAddress = NginxPod;
                return after(http);
            });
            next(app);
        };
    }
}

public static class Pg
{
    public static readonly string Master =
        Environment.GetEnvironmentVariable("EVAL_PG") ?? "Host=localhost;Username=postgres;Password=postgres;Database=postgres";

    // The schema as production has it; the service's model must still match it.
    public const string Schema = """
        CREATE TABLE clinic (id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY, name varchar(100) NOT NULL);
        CREATE TABLE visit (
            id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            clinic_id int NOT NULL REFERENCES clinic (id),
            patient_id int NOT NULL,
            day date NOT NULL,
            position int NOT NULL);
        CREATE INDEX ix_visit_clinic_day ON visit (clinic_id, day);
        """;

    public static void Execute(string connectionString, string sql)
    {
        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }
}

public static class Fixture
{
    public static string Dir => typeof(Fixture).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "FixtureDir").Value!;
}
