using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace HiddenTests;

/// <summary>One test per planted finding. Each fails on the original and passes once that finding is fixed.</summary>
[Trait("Kind", "Finding")]
public sealed class FindingTests
{
    [Fact(DisplayName = "M01 the vendor is asked at its v2 address")]
    public async Task M01_VendorPath()
    {
        using var pod = new Pod().Start();
        await pod.PatientWithMeterAsync("SN-1", Pod.Reading("r-1", "112"));
        await pod.SyncAsync();
        var asked = pod.Outside.Requests.Where(r => r.Uri.Host == "meters.example.net").Select(r => r.Uri.AbsolutePath).ToList();
        Assert.Contains("/v2/meters/SN-1/readings", asked);
    }

    [Fact(DisplayName = "M03 the SMS dates the reading in the Gregorian calendar")]
    public async Task M03_GregorianDate()
    {
        // The finding only shows where ICU gives ar-SA its Hijri calendar (Ubuntu 24.04's does).
        Assert.True(new CultureInfo("ar-SA").Calendar is UmAlQuraCalendar, "this machine's ICU gives ar-SA a Gregorian calendar");
        using var pod = new Pod().Start();
        await pod.PatientWithMeterAsync("SN-1", Pod.Reading("r-1", "50", takenAt: "2026-11-02T10:00:00Z"));
        await pod.SyncAsync();
        await pod.SweepAsync();
        var text = Assert.Single(pod.Outside.Sms).Text;
        Assert.Contains("2026", text, StringComparison.Ordinal);
        Assert.DoesNotContain("1448", text, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "M04 with two pods sweeping at once, each reading is texted once")]
    public async Task M04_TwoPods()
    {
        using var a = new Pod().Start();
        using var b = new Pod(sameDatabaseAs: a).Start();
        await a.PatientWithMeterAsync("SN-1", Pod.Reading("r-1", "40"), Pod.Reading("r-2", "45"), Pod.Reading("r-3", "55"));
        await a.SyncAsync();
        a.Outside.SmsDelay = b.Outside.SmsDelay = TimeSpan.FromMilliseconds(300);
        await Task.WhenAll(Task.Run(a.SweepAsync), Task.Run(b.SweepAsync));
        await Task.WhenAll(Task.Run(a.SweepAsync), Task.Run(b.SweepAsync));
        var sent = a.Outside.Sms.Concat(b.Outside.Sms).Select(s => s.Text).ToList();
        Assert.Equal(3, sent.Count);
        Assert.Equal(3, sent.Distinct().Count());
    }

    [Fact(DisplayName = "M05 a LO or HI reading texts the patient")]
    public async Task M05_HiLo()
    {
        using var pod = new Pod().Start();
        await pod.PatientWithMeterAsync("SN-1", Pod.Reading("r-1", "LO"), Pod.Reading("r-2", "HI", takenAt: "2026-11-02T11:00:00Z"));
        await pod.SyncAsync();
        await pod.SweepAsync();
        Assert.Equal(2, pod.Outside.Sms.Count);
    }

    [Fact(DisplayName = "M06 an mmol/L reading is judged by the mg/dL thresholds")]
    public async Task M06_Units()
    {
        using var pod = new Pod().Start();
        await pod.PatientWithMeterAsync("SN-1",
            Pod.Reading("r-1", "3.0", "mmol/L"),                                    // 54 mg/dL: low
            Pod.Reading("r-2", "5.5", "mmol/L", "2026-11-02T10:30:00Z"),            // 99 mg/dL: normal
            Pod.Reading("r-3", "15.0", "mmol/L", "2026-11-02T11:00:00Z"));          // 270 mg/dL: high
        await pod.SyncAsync();
        await pod.SweepAsync();
        var sms = pod.Outside.Sms;
        Assert.Equal(2, sms.Count);
        Assert.DoesNotContain(sms, s => s.Text.Contains("5.5", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "M07 a token issued for another service is refused")]
    public async Task M07_Audience()
    {
        using var pod = new Pod().Start();
        var id = await pod.AddPatientAsync(Patients.NationalId1, Patients.Phone1);
        var response = await pod.As(id, audience: "billing").GetAsync("/meters");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "M08 a site that isn't the clinic dashboard gets no CORS grant")]
    public async Task M08_Cors()
    {
        using var pod = new Pod().Start();
        var id = await pod.AddPatientAsync(Patients.NationalId1, Patients.Phone1);
        var client = pod.As(id);
        client.DefaultRequestHeaders.Add("Origin", "https://evil.example.org");
        var response = await client.GetAsync("/meters");
        Assert.False(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowed)
                     && allowed.Any(v => v is "*" or "https://evil.example.org"),
            "the response lets https://evil.example.org read it");
    }

    [Fact(DisplayName = "M09 a patient can't link a meter to another patient")]
    public async Task M09_LinkOwnOnly()
    {
        using var pod = new Pod().Start();
        var one = await pod.AddPatientAsync(Patients.NationalId1, Patients.Phone1);
        var two = await pod.AddPatientAsync(Patients.NationalId2, Patients.Phone2);
        await pod.As(one).PostAsJsonAsync("/meters", new { nationalId = Patients.NationalId2, serialNumber = "SN-X" });
        var meters = await pod.As(two).GetFromJsonAsync<JsonElement[]>("/meters");
        Assert.DoesNotContain(meters!, m => m.GetProperty("serialNumber").GetString() == "SN-X");
    }

    [Fact(DisplayName = "M10 the meter list holds up under concurrent requests")]
    public async Task M10_Concurrent()
    {
        using var pod = new Pod().Start();
        var id = await pod.PatientWithMeterAsync("SN-1");
        var client = pod.As(id);
        var statuses = await Task.WhenAll(Enumerable.Range(0, 60).Select(_ => Task.Run(async () =>
            (await client.GetAsync("/meters")).StatusCode)));
        Assert.All(statuses, s => Assert.Equal(HttpStatusCode.OK, s));
    }
}

/// <summary>Run in their own step, with an empty time-zone folder (TZDIR): an image with ICU but no tzdata.</summary>
[Trait("Kind", "Production")]
public sealed class ProductionTests
{
    [Fact(DisplayName = "M02 without tzdata, the SMS still goes out with the Riyadh time")]
    public async Task M02_NoTzdata()
    {
        TimeZoneInfo.ClearCachedData();
        Assert.Throws<TimeZoneNotFoundException>(() => TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh"));
        using var pod = new Pod().Start();
        await pod.PatientWithMeterAsync("SN-1", Pod.Reading("r-1", "50", takenAt: "2026-11-02T10:00:00Z"));
        await pod.SyncAsync();
        await pod.SweepAsync();
        var text = Assert.Single(pod.Outside.Sms).Text;
        Assert.Contains("13:00", text, StringComparison.Ordinal);
    }
}
