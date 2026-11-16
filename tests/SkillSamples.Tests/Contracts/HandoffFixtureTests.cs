using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.Contracts;

/// <summary>
/// api_handoff.py's tests (tests/apihandoff) read the visits API's documents from files. These keep those
/// files what ASP.NET Core generates: a change to the API, or to how .NET writes the document, fails here.
/// To refresh them, run this class with OPENAPI_FIXTURES set to tests/apihandoff/fixture.
/// </summary>
public sealed class HandoffFixtureTests
{
    public static TheoryData<string> Releases => new() { "v1", "v2", "v2-required" };

    [Theory]
    [MemberData(nameof(Releases))]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_TheHandoffReadsTheDocumentsAspNetCoreGenerates(string release)
    {
        // Given the release's API, started as the service would be
        await using var app = release switch
        {
            "v1" => await VisitsApi.StartV1Async(),
            "v2" => await VisitsApi.StartV2Async(),
            _ => await VisitsApi.StartV2Async(newRequiredField: true),
        };
        var generated = JsonNode.Parse(await app.GetTestClient().GetStringAsync("/openapi/v1.json"))!;

        var refresh = Environment.GetEnvironmentVariable("OPENAPI_FIXTURES");
        if (refresh is not null)
        {
            await File.WriteAllTextAsync(Path.Combine(refresh, release + ".json"),
                generated.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        }

        // Then the committed file is that document
        var committed = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Contracts", "handoff", release + ".json")))!;
        Assert.True(JsonNode.DeepEquals(generated, committed),
            $"tests/apihandoff/fixture/{release}.json is not what ASP.NET Core generates; refresh it with OPENAPI_FIXTURES");
    }
}
