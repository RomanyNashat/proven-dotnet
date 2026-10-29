using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace HiddenTests;

/// <summary>What works on the original and must keep working.</summary>
[Trait("Kind", "Regression")]
public sealed class RegressionTests
{
    [Fact(DisplayName = "R1 a clinician records a result and finds it in the list")]
    public async Task R1_RecordAndList()
    {
        using var pod = new Pod().Start();
        var client = pod.As("dr-a");
        var id = await Pod.RecordAsync(client, Pod.Result(test: "HbA1c", value: 6.1m, notes: "fasting"));
        var result = (await Pod.AllPagesAsync(client, limit: 50)).Single(r => r.GetProperty("id").GetInt32() == id);
        Assert.Equal("HbA1c", result.GetProperty("test").GetString());
        Assert.Equal(6.1m, result.GetProperty("value").GetDecimal());
        Assert.Equal("%", result.GetProperty("unit").GetString());
        Assert.Equal("fasting", result.GetProperty("notes").GetString());
    }

    [Fact(DisplayName = "R2 results recorded at different times come back newest first, across pages")]
    public async Task R2_Paging()
    {
        using var pod = new Pod().Start();
        var client = pod.As("dr-a");
        var ids = new List<int>();
        for (var i = 0; i < 5; i++)
        {
            ids.Add(await Pod.RecordAsync(client, Pod.Result()));
            pod.Time.Advance(TimeSpan.FromMinutes(1));
        }

        var seen = (await Pod.AllPagesAsync(client, limit: 2)).Select(r => r.GetProperty("id").GetInt32()).ToList();
        Assert.Equal(Enumerable.Reverse(ids), seen);
    }

    [Fact(DisplayName = "R3 a recorded result texts the patient once")]
    public async Task R3_TextOnce()
    {
        using var pod = new Pod().Start();
        await Pod.RecordAsync(pod.As("dr-a"), Pod.Result());
        await pod.WaitForSmsAsync(1);
        await Task.Delay(300);
        var sms = Assert.Single(pod.Outside.Sms);
        Assert.Equal(Pod.Phone, sms.To);
    }

    [Fact(DisplayName = "R4 a patient's token can't read the results")]
    public async Task R4_PatientRefused()
    {
        using var pod = new Pod().Start();
        Assert.Equal(HttpStatusCode.Forbidden, (await pod.As("p-1", role: "patient").GetAsync("/results")).StatusCode);
    }

    [Fact(DisplayName = "R5 the API needs a token; health is open")]
    public async Task R5_TokenAndHealth()
    {
        using var pod = new Pod().Start();
        Assert.Equal(HttpStatusCode.Unauthorized, (await pod.CreateClient().GetAsync("/results")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await pod.CreateClient().GetAsync("/health/live")).StatusCode);
    }

    [Fact(DisplayName = "R6 the lab lookup returns the lab's orders for that patient")]
    public async Task R6_Lookup()
    {
        using var pod = new Pod().Start();
        var orders = await (await pod.As("dr-a").PostAsJsonAsync("/lab-orders/lookup", new { nationalId = Pod.NationalId }))
            .Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.Equal("LAB-7781", Assert.Single(orders!).GetProperty("orderId").GetString());
        Assert.Contains(pod.Outside.Requests, r => r.Uri.ToString() == "https://labs.example.net/api/patients/1012345678/orders");
    }
}
