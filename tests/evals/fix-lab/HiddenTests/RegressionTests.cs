using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace HiddenTests;

/// <summary>What works on the original and must keep working.</summary>
[Trait("Kind", "Regression")]
public sealed class RegressionTests
{
    [Fact(DisplayName = "R1 a patient saves a reading and reads it back")]
    public async Task R1_SaveAndRead()
    {
        using var app = new Readings();
        var client = app.Start();
        var response = await client.PostAsJsonAsync("/readings", Readings.Reading(value: "6.2"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var reading = await client.GetFromJsonAsync<JsonElement>($"/readings/{id}");
        Assert.Equal(6.2m, reading.GetProperty("value").GetDecimal());
        Assert.Equal("mmol/L", reading.GetProperty("unit").GetString());
    }

    [Fact(DisplayName = "R2 today's chart has this morning's reading")]
    public async Task R2_Today()
    {
        using var app = new Readings();
        var client = app.Start();
        var id = await Readings.PostAsync(client, Readings.Reading(collectedAt: "2026-11-02T08:00:00Z"));
        var today = await client.GetFromJsonAsync<JsonElement[]>("/readings/today");
        Assert.Contains(today!, r => r.GetProperty("id").GetInt32() == id);
    }

    [Fact(DisplayName = "R3 a high reading reaches the care team once, at its alerts address")]
    public async Task R3_AlertOnce()
    {
        using var app = new Readings();
        var client = app.Start();
        await Readings.PostAsync(client, Readings.Reading(value: "12.5"));
        await Readings.PostAsync(client, Readings.Reading(value: "6.0"));
        await app.NotifyAsync();
        await app.NotifyAsync();
        var request = Assert.Single(app.CareTeam.Requests);
        Assert.Equal("https://careteam.example.net/api/alerts", request.Uri.ToString());
        Assert.Contains(Readings.NationalId, request.Body, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "R4 health is open and up when the database is")]
    public async Task R4_Health()
    {
        using var app = new Readings();
        app.Start();
        var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
    }

    [Fact(DisplayName = "R5 readings need a signed-in patient")]
    public async Task R5_NeedsToken()
    {
        using var app = new Readings();
        var client = app.Start(patient: null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/readings/today")).StatusCode);
    }

    [Fact(DisplayName = "R6 the app in Arabic gets its message in Arabic")]
    public async Task R6_ArabicMessage()
    {
        using var app = new Readings();
        var client = app.Start(language: "ar");
        var response = await client.PostAsJsonAsync("/readings", Readings.Reading(value: "6"));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("تم حفظ القراءة", body.GetProperty("message").GetString());
    }
}
