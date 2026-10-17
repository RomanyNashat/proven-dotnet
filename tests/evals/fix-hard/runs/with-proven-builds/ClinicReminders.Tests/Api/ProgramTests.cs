using System.Net;
using System.Net.Http.Json;
using ClinicReminders.Application;
using ClinicReminders.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace ClinicReminders.Tests.Api;

[Collection(nameof(HostCollection))]
public sealed class ProgramTests(ReminderApiFactory factory) : IClassFixture<ReminderApiFactory>
{
    [Fact]
    public async Task Health_NoToken_IsOkWithSecurityHeaders()
    {
        var response = await factory.CreateClient().GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().Equal("DENY");
    }

    [Fact]
    public async Task ListReminders_NoToken_IsUnauthorized()
    {
        var response = await factory.CreateClient().GetAsync("/reminders?cursor=");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UnmappedRoute_NoToken_IsUnauthorizedByTheFallbackPolicy()
    {
        var response = await factory.CreateClient().GetAsync("/reminders/1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListReminders_BadCursor_IsBadRequest()
    {
        var response = await Client("7").GetAsync("/reminders?cursor=not-a-cursor");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Should().ContainKey("cursor");
    }

    [Fact]
    public async Task ListReminders_SubIsNotAPatientId_IsForbidden()
    {
        var response = await Client("someone@example.com").GetAsync("/reminders");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ScheduleReminder_AnotherPatientsId_IsForbidden()
    {
        var response = await Client("7").PostAsJsonAsync("/reminders",
            new { patientId = 8, mobile = "0501234567", appointmentAt = DateTimeOffset.UtcNow.AddDays(2) });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ScheduleReminder_MobileTooLong_IsValidationProblem()
    {
        var response = await Client("7").PostAsJsonAsync("/reminders",
            new { patientId = 7, mobile = new string('5', 25), appointmentAt = DateTimeOffset.UtcNow.AddDays(2) });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Should().ContainKey("Mobile");
    }

    [Fact]
    public async Task ScheduleReminder_PastAppointment_IsValidationProblem()
    {
        var response = await Client("7").PostAsJsonAsync("/reminders",
            new { patientId = 7, mobile = "0501234567", appointmentAt = DateTimeOffset.UtcNow.AddHours(-1) });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public void SendDueRemindersJob_FromTheRootProvider_Resolves()
    {
        factory.Services.GetRequiredService<SendDueRemindersJob>().Should().NotBeNull();
    }

    [Fact]
    public async Task Scheduler_DailyTrigger_FiresAtNineRiyadhTime()
    {
        var scheduler = await factory.Services.GetRequiredService<ISchedulerFactory>().GetScheduler();

        var trigger = (ICronTrigger)(await scheduler.GetTrigger(new TriggerKey(SendDueRemindersJob.TriggerName)))!;
        var next = trigger.GetFireTimeAfter(new DateTimeOffset(2030, 3, 1, 0, 0, 0, TimeSpan.Zero));   // after the trigger starts

        next.Should().Be(new DateTimeOffset(2030, 3, 1, 6, 0, 0, TimeSpan.Zero));
        trigger.MisfireInstruction.Should().Be(MisfireInstruction.CronTrigger.FireOnceNow);
    }

    [Fact]
    public void LoggerFactory_InTheHost_IsTheRedactingOneNotSerilogs()
    {
        var logger = factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("test");

        logger.Should().NotBeNull();
        factory.Services.GetRequiredService<ILoggerFactory>().GetType().Name.Should().NotBe("SerilogLoggerFactory");
    }

    private HttpClient Client(string sub)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.Header, sub);
        return client;
    }
}
