using System.Net;
using System.Net.Http.Json;
using LabResults.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HiddenTests;

/// <summary>One test per planted finding. Each fails on the original and passes once that finding is fixed.</summary>
[Trait("Kind", "Finding")]
public sealed class FindingTests
{
    [Fact(DisplayName = "K01 a clinician with a Keycloak token gets the results")]
    public async Task K01_KeycloakRoles()
    {
        using var pod = new Pod().Start();
        var response = await pod.As("dr-a", flatRole: false).GetAsync("/results");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(DisplayName = "K02 results recorded in the same instant all come back across pages")]
    public async Task K02_PagingTies()
    {
        using var pod = new Pod().Start();
        var client = pod.As("dr-a");
        var ids = new List<int>();
        for (var i = 0; i < 5; i++)
        {
            ids.Add(await Pod.RecordAsync(client, Pod.Result()));   // the clock doesn't move: one instant
        }

        var seen = (await Pod.AllPagesAsync(client, limit: 2)).Select(r => r.GetProperty("id").GetInt32()).ToList();
        Assert.Equal(ids.Order(), seen.Order());
    }

    [Fact(DisplayName = "K03 looking up a patient's lab orders logs no national ID")]
    public async Task K03_HttpClientLogs()
    {
        using var pod = new Pod().Start();
        var response = await pod.As("dr-a").PostAsJsonAsync("/lab-orders/lookup", new { nationalId = Pod.NationalId });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(pod.Logs(), l => l.Contains(Pod.NationalId, StringComparison.Ordinal));
    }

    [Fact(DisplayName = "K04 a national ID typed in Arabic-Indic digits isn't logged either")]
    public async Task K04_ArabicIndicDigits()
    {
        using var pod = new Pod().Start();
        var response = await pod.As("dr-a").PostAsJsonAsync("/results", Pod.Result(nationalId: Pod.NationalIdArabic));
        Assert.True(response.IsSuccessStatusCode || (int)response.StatusCode == 400, $"POST /results: {(int)response.StatusCode}");
        Assert.DoesNotContain(pod.Logs(), l => l.Contains(Pod.NationalIdArabic, StringComparison.Ordinal)
                                               || l.Contains(Pod.NationalId, StringComparison.Ordinal));
    }

    [Fact(DisplayName = "K05 SMS still queued when the pod stops are sent")]
    public async Task K05_DrainOnShutdown()
    {
        using var database = new Pod();   // holds the database; the two pods share it, its clock and the outside
        var first = new Pod(sameDatabaseAs: database).Start();
        first.Outside.SmsDelay = TimeSpan.FromMilliseconds(300);
        var client = first.As("dr-a");
        for (var i = 0; i < 4; i++)
        {
            await Pod.RecordAsync(client, Pod.Result(test: "T" + i));
        }

        // A deploy: the pod stops with texts still waiting. If they were kept, the next pod may send them.
        await first.DisposeAsync();
        using var next = new Pod(sameDatabaseAs: database);
        next.Start();
        await next.WaitForSmsAsync(4);
        await Task.Delay(500);

        var texts = next.Outside.Sms.Select(s => s.Text).ToList();
        Assert.Equal(4, texts.Count);
        Assert.Equal(4, texts.Distinct().Count());
    }

    [Fact(DisplayName = "K06 each result records the clinician who recorded it")]
    public async Task K06_RecordedBy()
    {
        using var pod = new Pod().Start();
        var a = await Pod.RecordAsync(pod.As("dr-a"), Pod.Result(test: "A"));
        var b = await Pod.RecordAsync(pod.As("dr-b"), Pod.Result(test: "B"));

        // Each clinician reads their own result, so a list scoped to its caller passes too.
        var mine = await Pod.AllPagesAsync(pod.As("dr-a"), limit: 50);
        var theirs = await Pod.AllPagesAsync(pod.As("dr-b"), limit: 50);
        Assert.Equal("dr-a", mine.Single(r => r.GetProperty("id").GetInt32() == a).GetProperty("recordedBy").GetString());
        Assert.Equal("dr-b", theirs.Single(r => r.GetProperty("id").GetInt32() == b).GetProperty("recordedBy").GetString());
    }

    [Fact(DisplayName = "K07 every string column has a length of at most 4000")]
    public void K07_BoundedStrings()
    {
        using var pod = new Pod().Start();
        using var scope = pod.Services.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<LabDbContext>().Model;
        var unbounded = model.GetEntityTypes().SelectMany(e => e.GetProperties())
            .Where(p => p.ClrType == typeof(string) && (p.GetMaxLength() is null or > 4000))
            .Select(p => $"{p.DeclaringType.ClrType.Name}.{p.Name}").ToList();
        Assert.Empty(unbounded);
    }

    [Fact(DisplayName = "K08 a token that expired two minutes ago is refused")]
    public async Task K08_ClockSkew()
    {
        using var pod = new Pod().Start();
        var response = await pod.As("dr-a", expiresIn: TimeSpan.FromMinutes(-2)).GetAsync("/results");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
