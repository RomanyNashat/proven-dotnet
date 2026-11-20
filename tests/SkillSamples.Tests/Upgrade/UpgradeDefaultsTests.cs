using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Npgsql;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.Upgrade;

// Defaults a .NET 8 service meets on 10 that no compiler error points at, on the runtime these tests run on.
public sealed class UpgradeDefaultsTests
{
    private sealed class VisitsContext(DbContextOptions<VisitsContext> options) : DbContext(options);

    private static IConfiguration Json(params string[] files)
    {
        var builder = new ConfigurationBuilder();
        foreach (var file in files) builder.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(file)));
        return builder.Build();
    }

    private static ReminderSettings Bound(IConfiguration configuration) =>
        configuration.GetSection(ReminderSettings.Section).Get<ReminderSettings>() ?? new ReminderSettings();

    [Fact]
    public void Config_JsonNull_BindsOverTheDefault()
    {
        var settings = Bound(Json("""{ "Reminders": { "Sender": "Visits", "Retries": 3 } }""",
                                  """{ "Reminders": { "Sender": null, "Retries": null } }"""));

        Assert.Null(settings.Sender);
        Assert.Equal(0, settings.Retries);
    }

    [Fact]
    public void Config_EmptyEnvironmentVariable_IsAValue_AndWinsOverTheJson()
    {
        Environment.SetEnvironmentVariable("UPGRADE_DEFAULTS__Reminders__Sender", "");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes("""{ "Reminders": { "Sender": "Visits" } }""")))
                .AddEnvironmentVariables("UPGRADE_DEFAULTS__")
                .Build();

            Assert.Equal("", Bound(configuration).Sender);
        }
        finally
        {
            Environment.SetEnvironmentVariable("UPGRADE_DEFAULTS__Reminders__Sender", null);
        }
    }

    [Fact]
    public void Npgsql_ConnectionLifetime_DefaultsToAnHour()
    {
        Assert.Equal(3600, new NpgsqlConnectionStringBuilder("Host=db;Database=visits").ConnectionLifetime);
    }

    [Fact]
    public void EfCoreSqlServer_ConnectionStringWithoutApplicationName_DiffersFromSqlClients()
    {
        // The pool is keyed by the connection string, so EF Core and Dapper reading the same setting land
        // in two pools, and the DBA sees "EFCore/10..." instead of the service.
        const string setting = "Server=db;Database=Visits;User Id=visits;Password=x;TrustServerCertificate=true";
        using var context = new VisitsContext(new DbContextOptionsBuilder<VisitsContext>().UseSqlServer(setting).Options);

        var efs = context.Database.GetDbConnection().ConnectionString;

        Assert.Contains("Application Name=\"EFCore/10.", efs, StringComparison.Ordinal);
        Assert.NotEqual(new SqlConnection(setting).ConnectionString, efs);
    }

    [Fact]
    public void EfCoreSqlServer_ApplicationNameInTheSetting_IsKept_SoBothShareOnePool()
    {
        const string setting = "Server=db;Database=Visits;User Id=visits;Password=x;TrustServerCertificate=true;Application Name=visits-api";
        using var context = new VisitsContext(new DbContextOptionsBuilder<VisitsContext>().UseSqlServer(setting).Options);

        Assert.Equal(new SqlConnection(setting).ConnectionString, context.Database.GetDbConnection().ConnectionString);
    }

    [Fact]
    public async Task HttpClientFactoryLogs_HideTheQueryString_AndHeaderValues()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddFakeLogging().SetMinimumLevel(LogLevel.Trace));
        services.AddHttpClient("records").ConfigurePrimaryHttpMessageHandler(() => new Answers200());
        await using var provider = services.BuildServiceProvider();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://records.test/patients?nationalId=1012345672");
        request.Headers.Add("X-Api-Key", "the-key");

        await provider.GetRequiredService<IHttpClientFactory>().CreateClient("records").SendAsync(request);

        var logged = string.Join("\n", provider.GetFakeLogCollector().GetSnapshot().Select(r => r.Message));
        Assert.Contains("https://records.test/patients?*", logged, StringComparison.Ordinal);
        Assert.Contains("X-Api-Key: *", logged, StringComparison.Ordinal);
        Assert.DoesNotContain("1012345672", logged, StringComparison.Ordinal);
        Assert.DoesNotContain("the-key", logged, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_AProductionFileNullsASetting_TheDeployStopsInsteadOfSendingWithNoRetries()
    {
        // Given: after the move to .NET 10, appsettings.Production.json "clears" the retries with null, as
        // it did on 8 to fall back to the default
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes("""{ "Reminders": { "Retries": null } }""")));
        builder.Services.AddReminders();

        // When: the service starts
        using var host = builder.Build();
        var start = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        // Then: it refuses to start and names the setting, rather than sending every reminder once
        Assert.Contains(nameof(ReminderSettings.Retries), start.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public async Task Production_SlimImage_AnEmptyVariableStopsTheStart()
    {
        ProductionConditions.Require();
        Environment.SetEnvironmentVariable("Reminders__Sender", "");
        try
        {
            var builder = Host.CreateApplicationBuilder();
            builder.Services.AddReminders();
            using var host = builder.Build();

            var start = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
            Assert.Contains(nameof(ReminderSettings.Sender), start.Message, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Reminders__Sender", null);
        }
    }

    private sealed class Answers200 : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}
