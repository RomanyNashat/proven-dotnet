using System.Net;
using System.Text.Json;
using Xunit;

namespace HiddenTests;

/// <summary>What the service already did right. Pass on the original; a fix must not break them.</summary>
public sealed class RegressionTests
{
    private static readonly DateTimeOffset Appointment = new(2026, 11, 3, 6, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task R1_APatient_SchedulesAndSeesTheirReminder()
    {
        using var clinic = new Clinic();
        var client = await clinic.StartAsync();

        var created = await Clinic.ScheduleAsync(client, 1, Appointment);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var page = JsonDocument.Parse(await client.GetStringAsync("/reminders"));
        Assert.Single(page.RootElement.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task R2_ADueReminder_SendsOneSmsWithTheLocalTime()
    {
        using var clinic = new Clinic();
        var client = await clinic.StartAsync();
        (await Clinic.ScheduleAsync(client, 1, Appointment)).EnsureSuccessStatusCode();

        await clinic.RunJobAsync();

        var sent = Assert.Single(await clinic.SmsAsync(1));
        Assert.Contains("09:30", FindingTests.Text(sent.Body), StringComparison.Ordinal);
        Assert.Contains(Clinic.Mobile, sent.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task R3_ALaterAppointment_IsNotSentYet()
    {
        using var clinic = new Clinic();
        var client = await clinic.StartAsync();
        (await Clinic.ScheduleAsync(client, 1, Appointment.AddDays(3))).EnsureSuccessStatusCode();

        await clinic.RunJobAsync();

        Assert.Empty(await clinic.SmsAsync(0));
    }

    [Fact]
    public async Task R4_Health_IsOpen()
    {
        using var clinic = new Clinic();
        await clinic.StartAsync();
        using var anonymous = clinic.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task R5_Reminders_NeedASignedInUser()
    {
        using var clinic = new Clinic();
        await clinic.StartAsync();
        using var anonymous = clinic.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/reminders")).StatusCode);
    }
}
