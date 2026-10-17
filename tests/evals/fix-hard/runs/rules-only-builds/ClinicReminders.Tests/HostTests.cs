using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using ClinicReminders.Application;
using ClinicReminders.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace ClinicReminders.Tests;

/// <summary>
/// The host in the Production environment, without a database: everything here is decided before a query runs.
/// The database paths (paging, the send job) need SQL Server and are not covered here.
/// </summary>
public sealed class HostTests(HostTests.Factory factory) : IClassFixture<HostTests.Factory>
{
    [Fact]
    public async Task Health_Anonymous_IsOk()
    {
        var response = await factory.CreateClient().GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reminders_NoCaller_IsUnauthorized()
    {
        var response = await factory.CreateClient().GetAsync("/reminders");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Reminders_CallerWithoutNumericSubject_IsForbidden()
    {
        var response = await Caller("not-a-number").GetAsync("/reminders");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reminders_CursorNotFromTheApi_IsBadRequest()
    {
        var response = await Caller("7").GetAsync("/reminders?cursor=2026-10-17T09:00:00Z");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Schedule_ForAnotherPatient_IsForbidden()
    {
        var response = await Caller("7").PostAsJsonAsync("/reminders", Body(patientId: 8));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Schedule_AppointmentInThePast_IsBadRequest()
    {
        var response = await Caller("7").PostAsJsonAsync("/reminders", Body(patientId: 7, appointmentAt: DateTimeOffset.UtcNow.AddHours(-1)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Schedule_AppointmentWithoutOffset_IsBadRequest()
    {
        var response = await Caller("7").PostAsync("/reminders", JsonContent.Create(new
        {
            patientId = 7,
            mobile = "+966500000001",
            appointmentAt = DateTime.UtcNow.AddDays(2).ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
        }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public void SendDueRemindersJob_ResolvesFromAScope()
    {
        using var scope = factory.Services.CreateScope();

        var job = scope.ServiceProvider.GetRequiredService<SendDueRemindersJob>();

        job.Should().NotBeNull();
    }

    [Fact]
    public void SmsClient_IsATypedClientUnderTheVersionPath()
    {
        using var scope = factory.Services.CreateScope();
        var sms = factory.Services.GetRequiredService<IOptions<SmsOptions>>().Value;

        scope.ServiceProvider.GetRequiredService<SmsClient>().Should().NotBeNull();
        sms.BaseAddress.AbsoluteUri.Should().EndWith("/api/v2/");
    }

    [Fact]
    public void Logging_GoesThroughSerilog()
    {
        factory.Services.GetRequiredService<ILoggerFactory>().GetType().FullName.Should().StartWith("Serilog");
    }

    [Fact]
    public async Task SendTrigger_FiresOnRiyadhTime()
    {
        var scheduler = await factory.Services.GetRequiredService<ISchedulerFactory>().GetScheduler();

        var triggers = await scheduler.GetTriggersOfJob(new JobKey("send-due-reminders"));

        triggers.Should().ContainSingle().Which.Should().BeAssignableTo<ICronTrigger>()
            .Which.TimeZone.BaseUtcOffset.Should().Be(TimeSpan.FromHours(3));
    }

    private static object Body(int patientId, DateTimeOffset? appointmentAt = null) => new
    {
        patientId,
        mobile = "+966500000001",
        appointmentAt = appointmentAt ?? DateTimeOffset.UtcNow.AddDays(2),
    };

    private HttpClient Caller(string subject)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuth.SubjectHeader, subject);
        return client;
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Reminders", "Server=127.0.0.1,1;Database=Reminders;Connect Timeout=1;TrustServerCertificate=true");
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(TestAuth.Scheme).AddScheme<AuthenticationSchemeOptions, TestAuth>(TestAuth.Scheme, _ => { });
                services.Configure<AuthenticationOptions>(o =>
                {
                    o.DefaultScheme = TestAuth.Scheme;
                    o.DefaultAuthenticateScheme = TestAuth.Scheme;
                    o.DefaultChallengeScheme = TestAuth.Scheme;
                    o.DefaultForbidScheme = TestAuth.Scheme;
                });
            });
        }
    }

    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Scheme = "Test";
        public const string SubjectHeader = "X-Test-Sub";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(SubjectHeader, out var subject))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity([new Claim("sub", subject.ToString())], Scheme);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
        }
    }
}
