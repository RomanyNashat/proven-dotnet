using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HomeReadings.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HiddenTests;

/// <summary>One test per planted finding. Each fails on the original service.</summary>
[Trait("Kind", "Finding")]
public sealed class FindingTests
{
    private static DateTimeOffset CollectedAt(JsonElement reading) =>
        DateTimeOffset.Parse(reading.GetProperty("collectedAt").GetString()!, System.Globalization.CultureInfo.InvariantCulture);

    [Fact(DisplayName = "H01 a reading sent with the phone's offset is stored at the right instant")]
    public async Task H01_LocalTime()
    {
        using var app = new Readings();
        var client = app.Start();
        var id = await Readings.PostAsync(client, Readings.Reading(collectedAt: "2026-11-02T08:00:00+03:00"));
        var reading = await client.GetFromJsonAsync<JsonElement>($"/readings/{id}");
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 5, 0, 0, TimeSpan.Zero), CollectedAt(reading));
    }

    [Fact(DisplayName = "H02 a care team call that fails with 503 isn't repeated within one run")]
    public async Task H02_NoRetryOnPost()
    {
        using var app = new Readings();
        var client = app.Start();
        app.CareTeam.Respond = (_, before) => before == 0 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK;
        await Readings.PostAsync(client, Readings.Reading(value: "12.5"));
        await app.NotifyAsync();
        Assert.Single(app.CareTeam.Requests);
    }

    [Fact(DisplayName = "H03 one alert the care team rejects doesn't stop the others")]
    public async Task H03_PoisonAlert()
    {
        using var app = new Readings();
        var client = app.Start();
        app.CareTeam.Respond = (body, _) => body.Contains("1000000001", StringComparison.Ordinal) ? HttpStatusCode.BadRequest : HttpStatusCode.OK;
        await Readings.PostAsync(client, Readings.Reading(value: "13.0", nationalId: "1000000001"));
        await Readings.PostAsync(client, Readings.Reading(value: "14.0", nationalId: "1000000002"));
        for (var run = 0; run < 3; run++)
        {
            await app.NotifyAsync();
        }

        Assert.Contains(app.CareTeam.Requests, r => r.Body.Contains("1000000002", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "H04 the latest reading shown to one patient is never another's")]
    public async Task H04_CacheLeak()
    {
        using var app = new Readings();
        app.Start();
        var own = await Readings.PostAsync(app.As(2), Readings.Reading(value: "7.7", nationalId: "1099999999"));
        var other = await Readings.PostAsync(app.As(1), Readings.Reading(value: "6.1"));
        (await app.As(1).GetAsync("/readings/latest")).EnsureSuccessStatusCode();
        var latest = await app.As(2).GetFromJsonAsync<JsonElement>("/readings/latest");
        Assert.NotEqual(other, latest.GetProperty("id").GetInt32());
        Assert.Equal(own, latest.GetProperty("id").GetInt32());
    }

    [Fact(DisplayName = "H05 a patient can't read another patient's reading by its id")]
    public async Task H05_Idor()
    {
        using var app = new Readings();
        app.Start();
        var id = await Readings.PostAsync(app.As(1), Readings.Reading());
        var response = await app.As(2).GetAsync($"/readings/{id}");
        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden, $"{(int)response.StatusCode}");
    }

    [Fact(DisplayName = "H06 an app in Arabic sends 5.4 and the reading is 5.4")]
    public async Task H06_ArabicNumbers()
    {
        using var app = new Readings();
        var client = app.Start(language: "ar");
        var id = await Readings.PostAsync(client, Readings.Reading(value: "5.4"));
        var reading = await app.As(1).GetFromJsonAsync<JsonElement>($"/readings/{id}");
        Assert.Equal(5.4m, reading.GetProperty("value").GetDecimal());
    }

    [Fact(DisplayName = "H07 'today' is the patient's day in Riyadh, after midnight there")]
    public async Task H07_TodayInRiyadh()
    {
        using var app = new Readings();
        app.Time.SetUtcNow(new DateTimeOffset(2026, 11, 2, 22, 30, 0, TimeSpan.Zero));   // 01:30 on 3 November in Riyadh
        var client = app.Start();
        var afterMidnight = await Readings.PostAsync(client, Readings.Reading(collectedAt: "2026-11-02T21:30:00Z"));   // 00:30 on the 3rd
        var lastNight = await Readings.PostAsync(client, Readings.Reading(collectedAt: "2026-11-02T20:30:00Z"));      // 23:30 on the 2nd
        var today = await client.GetFromJsonAsync<JsonElement[]>("/readings/today");
        var ids = today!.Select(r => r.GetProperty("id").GetInt32()).ToList();
        Assert.Contains(afterMidnight, ids);
        Assert.DoesNotContain(lastNight, ids);
    }

    [Fact(DisplayName = "H08 with the database down, liveness stays up and readiness goes down")]
    public async Task H08_Liveness()
    {
        using var app = new Readings(databaseDown: true);
        var client = app.CreateClient();
        var live = await client.GetAsync("/health/live");
        var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, ready.StatusCode);
    }

    [Fact(DisplayName = "H09 a photo's file name can't write outside the photo folder")]
    public async Task H09_PathTraversal()
    {
        using var app = new Readings();
        var client = app.Start();
        var id = await Readings.PostAsync(client, Readings.Reading());
        var name = $"escaped-{Guid.NewGuid():N}.jpg";
        var escaped = Path.GetFullPath(Path.Combine(app.PhotoRoot, id.ToString(System.Globalization.CultureInfo.InvariantCulture), "../../../" + name));
        using var form = new MultipartFormDataContent();
        var photo = new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46]);
        photo.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(photo, "photo", "../../../" + name);
        await client.PostAsync($"/readings/{id}/photo", form);
        Assert.False(File.Exists(escaped), $"written outside the photo folder: {escaped}");
    }

    [Fact(DisplayName = "H10 the national ID reaches no log line")]
    public async Task H10_NationalIdNotLogged()
    {
        using var app = new Readings();
        var client = app.Start();
        await Readings.PostAsync(client, Readings.Reading(value: "12.0"));
        await app.NotifyAsync();
        await Task.Delay(500);
        Assert.DoesNotContain(app.Logs(), line => line.Contains(Readings.NationalId, StringComparison.Ordinal));
    }

    [Fact(DisplayName = "H11 a token that expired two minutes ago is refused")]
    public async Task H11_ClockSkew()
    {
        using var app = new Readings();
        var client = app.Start(patient: null);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Readings.Token(1, TimeSpan.FromMinutes(-2)));
        var response = await client.GetAsync("/readings/today");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "H12 an error's response says nothing about the database")]
    public async Task H12_ErrorLeak()
    {
        using var app = new Readings(databaseDown: true);
        var client = app.As(1);
        var response = await client.GetAsync("/readings/today");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode >= 500, $"{(int)response.StatusCode} {body}");
        foreach (var leak in new[] { "Npgsql", "Exception", "Failed to connect", "127.0.0.1", "localhost", "refused", "   at " })
        {
            Assert.DoesNotContain(leak, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact(DisplayName = "H13 every string column is bounded (at most 4000)")]
    public void H13_BoundedStrings()
    {
        using var app = new Readings();
        using var scope = app.Services.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<ReadingsDbContext>().Model;
        var unbounded = model.GetEntityTypes().SelectMany(e => e.GetProperties())
            .Where(p => p.ClrType == typeof(string) && (p.GetMaxLength() is not int n || n > 4000))
            .Select(p => $"{p.DeclaringType.DisplayName()}.{p.Name}")
            .ToList();
        Assert.Empty(unbounded);
    }
}
