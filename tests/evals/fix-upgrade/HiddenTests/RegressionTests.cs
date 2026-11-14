using System.Net;
using System.Net.Http.Json;
using ClinicQueue.Application;

namespace HiddenTests;

/// <summary>What works on the original and must still work after the upgrade.</summary>
public sealed class RegressionTests
{
    private const string Day = "2026-11-16";

    [Fact(DisplayName = "R1 a patient books a visit and gets the next position")]
    public async Task R1_Book()
    {
        await using var pod = new Pod();
        using var first = await pod.As(7).PostAsJsonAsync("/visits", new { clinicId = 1, day = Day });
        using var second = await pod.As(8).PostAsJsonAsync("/visits", new { clinicId = 1, day = Day });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(2, (await second.Content.ReadFromJsonAsync<Pod.Booked>())!.Position);
    }

    [Fact(DisplayName = "R2 another patient's visit is not found")]
    public async Task R2_Ownership()
    {
        await using var pod = new Pod();
        var visit = await pod.BookAsync(pod.As(7), 1, Day);

        using var response = await pod.As(8).GetAsync($"/visits/{visit}/day");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(DisplayName = "R3 the API needs a token for clinic-queue")]
    public async Task R3_Auth()
    {
        await using var pod = new Pod();

        using var none = await pod.CreateClient().GetAsync("/clinics");
        using var other = await pod.As(7, "another-service").GetAsync("/clinics");

        Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, other.StatusCode);
    }

    [Fact(DisplayName = "R4 health is open")]
    public async Task R4_Health()
    {
        await using var pod = new Pod();

        using var response = await pod.CreateClient().GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(DisplayName = "R5 the clinic list comes from the database")]
    public async Task R5_Clinics()
    {
        await using var pod = new Pod();
        var client = pod.As(7);

        // Waits for the list, so this passes whether or not it's ready at start (U03 is about that).
        ClinicEntry[] clinics = [];
        for (var i = 0; i < 50 && clinics.Length == 0; i++)
        {
            clinics = (await client.GetFromJsonAsync<ClinicEntry[]>("/clinics"))!;
            if (clinics.Length == 0)
            {
                await Task.Delay(100);
            }
        }

        Assert.Equal(["Cardiology", "Dermatology"], clinics.Select(c => c.Name));
    }

    [Fact(DisplayName = "R6 a day that isn't yyyy-MM-dd is a 400")]
    public async Task R6_BadDay()
    {
        await using var pod = new Pod();

        using var response = await pod.As(7).PostAsJsonAsync("/visits", new { clinicId = 1, day = "16/11/2026" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
