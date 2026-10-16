using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.Contracts;

/// <summary>What a release does to the apps already installed on phones, which can't be updated with it.</summary>
public sealed class ContractTests
{
    // The installed app reads responses with its release-1 model, strictly, as typed mobile clients do.
    private static readonly JsonSerializerOptions InstalledApp = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        RespectRequiredConstructorParameters = true,
    };

    private static readonly string Levels = Path.Combine(AppContext.BaseDirectory, "Contracts", "oasdiff-levels.txt");

    private static async Task<string> GetAsync(WebApplication app, string path) =>
        await app.GetTestClient().GetStringAsync(path);

    private static async Task<string> OpenApiFileAsync(WebApplication app, string folder, string name)
    {
        var path = Path.Combine(folder, name);
        await File.WriteAllTextAsync(path, await GetAsync(app, "/openapi/v1.json"));
        return path;
    }

    private static async Task<(int ExitCode, string Output)> OasdiffAsync(params string[] arguments)
    {
        var oasdiff = Environment.GetEnvironmentVariable("OASDIFF")
            ?? throw new InvalidOperationException("Set OASDIFF to the oasdiff binary (CI downloads it).");
        var start = new ProcessStartInfo(oasdiff) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync() + await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, output);
    }

    [Fact]
    public async Task OptionalFieldAdded_TheInstalledAppStillReadsTheVisit()
    {
        await using var api = await VisitsApi.StartV2Async();

        var visit = JsonSerializer.Deserialize<V1.Visit>(await GetAsync(api, "/visits/1"), InstalledApp)!;

        Assert.Equal((1, V1.VisitStatus.Booked, "Dr Sara"), (visit.Id, visit.Status, visit.Doctor));
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_ANewStatusShips_TheInstalledAppCantReadTheVisit()
    {
        // Given a release that adds "Cancelled" to the visit's status
        await using var api = await VisitsApi.StartV2Async();

        // When a patient on last month's app opens a cancelled visit
        var body = await GetAsync(api, "/visits/2");

        // Then the app can't read it at all: one new enum value is a breaking change for installed apps
        Assert.Contains("\"Cancelled\"", body);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<V1.Visit>(body, InstalledApp));
    }

    [Fact]
    public void FieldRemoved_IsAnErrorWhenTheModelRequiresIt_AndSilentWhenNot()
    {
        const string withoutDoctor = """{"id":1,"status":"Booked"}""";
        var lenient = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<V1.Visit>(withoutDoctor, InstalledApp));
        Assert.Null(JsonSerializer.Deserialize<V1.Visit>(withoutDoctor, lenient)!.Doctor);   // shown as blank, no error
    }

    [Fact]
    public async Task NewRequiredRequestField_EveryInstalledAppsRequestIsRejected()
    {
        await using var compatible = await VisitsApi.StartV2Async();
        await using var breaking = await VisitsApi.StartV2Async(newRequiredField: true);
        const string releaseOneRequest = """{"patientId":5,"note":"knee"}""";

        using var accepted = await compatible.GetTestClient().PostAsync("/visits", new StringContent(releaseOneRequest, Encoding.UTF8, "application/json"));
        using var rejected = await breaking.GetTestClient().PostAsync("/visits", new StringContent(releaseOneRequest, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }

    [Fact]
    public async Task DeprecatedEndpoint_AnswersWithDeprecationSunsetAndTheReplacement()
    {
        await using var api = await VisitsApi.StartV2Async();

        using var response = await api.GetTestClient().GetAsync("/visits/1");

        Assert.Equal("@1788220800", response.Headers.GetValues("Deprecation").Single());
        Assert.Equal("Mon, 01 Mar 2027 00:00:00 GMT", response.Headers.GetValues("Sunset").Single());
        Assert.Equal("</v2/visits/{id}>; rel=\"deprecation\"", response.Headers.GetValues("Link").Single());
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_OasdiffAtItsDefaultLetsTheNewStatusThrough_TheLevelsFileStopsIt()
    {
        // Given the OpenAPI documents of both releases, as ASP.NET Core generates them
        var folder = Directory.CreateTempSubdirectory("contracts-").FullName;
        await using var v1 = await VisitsApi.StartV1Async();
        await using var v2 = await VisitsApi.StartV2Async();
        var before = await OpenApiFileAsync(v1, folder, "v1.json");
        var after = await OpenApiFileAsync(v2, folder, "v2.json");

        // When the pipeline compares them with oasdiff
        var atDefault = await OasdiffAsync("breaking", "--fail-on", "ERR", before, after);
        var withLevels = await OasdiffAsync("breaking", "--fail-on", "ERR", "--severity-levels", Levels, before, after);

        // Then the default only warns, and the merge goes through; with the levels file the change fails it
        Assert.Equal(0, atDefault.ExitCode);
        Assert.Contains("response-property-enum-value-added", atDefault.Output);
        Assert.Equal(1, withLevels.ExitCode);
        Directory.Delete(folder, recursive: true);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public async Task Production_NoIcuNoTzdata_TheSunsetHeaderIsTheSameHttpDate()
    {
        ProductionConditions.Require();
        await using var api = await VisitsApi.StartV2Async();

        using var response = await api.GetTestClient().GetAsync("/visits/1");

        Assert.Equal("Mon, 01 Mar 2027 00:00:00 GMT", response.Headers.GetValues("Sunset").Single());
    }
}
