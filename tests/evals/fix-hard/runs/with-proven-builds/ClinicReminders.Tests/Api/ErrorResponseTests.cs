using System.Net;
using System.Net.Http.Json;
using ClinicReminders.Tests.Support;
using Microsoft.AspNetCore.Mvc;

namespace ClinicReminders.Tests.Api;

[Collection(nameof(HostCollection))]
public sealed class ErrorResponseTests
{
    [Fact]
    public async Task ScheduleReminder_DatabaseUnreachable_IsAProblemWithNothingInternal()
    {
        await using var factory = new ReminderApiFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.Header, "7");

        var response = await client.PostAsJsonAsync("/reminders",
            new { mobile = "0501234567", appointmentAt = DateTimeOffset.UtcNow.AddDays(2) });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");   // still there on a 500
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("traceId").And.NotContain("Sql").And.NotContain("0501234567");
    }

    [Fact]
    public async Task ScheduleReminder_OverTheCallersLimit_IsTooManyRequests()
    {
        await using var factory = new ReminderApiFactory();
        factory.Settings["RateLimits:SchedulePerMinute"] = "1";
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.Header, "7");
        var invalid = new { mobile = "x", appointmentAt = DateTimeOffset.UtcNow.AddDays(2) };   // never reaches the database

        var first = await client.PostAsJsonAsync("/reminders", invalid);
        var second = await client.PostAsJsonAsync("/reminders", invalid);

        first.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await second.Content.ReadFromJsonAsync<ProblemDetails>())!.Status.Should().Be(429);
    }
}
