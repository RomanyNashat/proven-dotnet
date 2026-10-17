using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicReminders.Tests.Support;

/// <summary>
/// The service in Production with a test sign-in. There is no SQL Server here, so these tests only cover
/// what happens before the database: start-up, auth, validation, headers, logging.
/// </summary>
public class ReminderApiFactory : WebApplicationFactory<Program>
{
    public Dictionary<string, string?> Settings { get; } = new()
    {
        ["ConnectionStrings:Reminders"] = "Server=127.0.0.1,1;Database=Reminders;Encrypt=False;Connect Timeout=1",
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(Settings));
        builder.ConfigureServices(services => services
            .AddAuthentication(o =>
            {
                o.DefaultScheme = TestAuthHandler.Scheme;
                o.DefaultAuthenticateScheme = TestAuthHandler.Scheme;
                o.DefaultChallengeScheme = TestAuthHandler.Scheme;
                o.DefaultForbidScheme = TestAuthHandler.Scheme;
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { }));
    }
}
