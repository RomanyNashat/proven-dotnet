using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace HiddenTests;

/// <summary>What works on the original and must keep working.</summary>
[Trait("Kind", "Regression")]
public sealed class RegressionTests
{
    [Fact(DisplayName = "R1 a patient links a meter and sees it")]
    public async Task R1_LinkAndList()
    {
        using var pod = new Pod().Start();
        var id = await pod.PatientWithMeterAsync("SN-1");
        var meters = await pod.As(id).GetFromJsonAsync<JsonElement[]>("/meters");
        Assert.Contains(meters!, m => m.GetProperty("serialNumber").GetString() == "SN-1");
    }

    [Fact(DisplayName = "R2 a low reading texts the patient once, a normal one doesn't")]
    public async Task R2_LowTextsOnce()
    {
        using var pod = new Pod().Start();
        await pod.PatientWithMeterAsync("SN-1", Pod.Reading("r-1", "50"), Pod.Reading("r-2", "112", takenAt: "2026-11-02T11:00:00Z"));
        await pod.SyncAsync();
        await pod.SweepAsync();
        await pod.SweepAsync();
        var sms = Assert.Single(pod.Outside.Sms);
        Assert.Equal(Patients.Phone1, sms.To);
        Assert.Contains("50", sms.Text, StringComparison.Ordinal);
        Assert.Contains(pod.Outside.Requests, r => r.Uri.ToString() == "https://sms.example.net/messages");
    }

    [Fact(DisplayName = "R3 a reading synced twice is stored and texted once")]
    public async Task R3_SyncTwice()
    {
        using var pod = new Pod().Start();
        await pod.PatientWithMeterAsync("SN-1", Pod.Reading("r-1", "300"));
        await pod.SyncAsync();
        await pod.SyncAsync();
        await pod.SweepAsync();
        Assert.Single(pod.Outside.Sms);
    }

    [Fact(DisplayName = "R4 the clinic dashboard gets its CORS grant")]
    public async Task R4_ClinicCors()
    {
        using var pod = new Pod().Start();
        var id = await pod.AddPatientAsync(Patients.NationalId1, Patients.Phone1);
        var client = pod.As(id);
        client.DefaultRequestHeaders.Add("Origin", Pod.ClinicOrigin);
        var response = await client.GetAsync("/meters");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowed), "no Access-Control-Allow-Origin");
        Assert.Contains(Pod.ClinicOrigin, allowed);
    }

    [Fact(DisplayName = "R5 the API needs a token, and takes one for its own audience")]
    public async Task R5_Tokens()
    {
        using var pod = new Pod().Start();
        var id = await pod.AddPatientAsync(Patients.NationalId1, Patients.Phone1);
        Assert.Equal(HttpStatusCode.Unauthorized, (await pod.CreateClient().GetAsync("/meters")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await pod.As(id).GetAsync("/meters")).StatusCode);
    }

    [Fact(DisplayName = "R6 health is open")]
    public async Task R6_Health()
    {
        using var pod = new Pod().Start();
        Assert.Equal(HttpStatusCode.OK, (await pod.CreateClient().GetAsync("/health/live")).StatusCode);
    }
}
