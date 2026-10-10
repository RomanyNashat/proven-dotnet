using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.Api;

public sealed class ApiTests
{
    private static async Task<WebApplication> StartAsync(Action<WebApplication> map)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<UnexpectedExceptionHandler>();
        builder.Services.AddPerClientRateLimits(otpPerWindow: 2);
        var app = builder.Build();
        app.UseExceptionHandler();
        app.Use((http, next) =>
        {
            // stand-in for authentication: the test says who the caller is
            if (http.Request.Headers.TryGetValue("x-test-user", out var user))
            {
                http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user!)], "test"));
            }

            return next(http);
        });
        app.UseRateLimiter();
        map(app);
        await app.StartAsync();
        return app;
    }

    [Fact]
    public async Task UnexpectedException_ReturnsProblemDetails_WithoutTheMessage()
    {
        await using var app = await StartAsync(a =>
            a.MapGet("/boom", (Func<string>)(() => throw new InvalidOperationException("patient 1089234567 has no record"))));
        var client = app.GetTestClient();

        using var response = await client.GetAsync("/boom");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.DoesNotContain("1089234567", body);
        Assert.DoesNotContain("InvalidOperationException", body);
        Assert.Contains("traceId", body);
    }

    [Fact]
    public async Task RateLimit_IsPerCaller_NotOneBucketForEveryone()
    {
        await using var app = await StartAsync(a => a.MapPost("/otp", () => TypedResults.Ok()).RequireRateLimiting(PerClientRateLimits.Otp));
        var client = app.GetTestClient();

        async Task<HttpStatusCode> SendAs(string user)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/otp");
            request.Headers.Add("x-test-user", user);
            using var response = await client.SendAsync(request);
            return response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.OK, await SendAs("7"));
        Assert.Equal(HttpStatusCode.OK, await SendAs("7"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await SendAs("7"));
        Assert.Equal(HttpStatusCode.OK, await SendAs("8"));   // another user still has their own quota
    }

    [Fact]
    public async Task RateLimit_Rejected_IsProblemDetailsWithRetryAfter()
    {
        await using var app = await StartAsync(a => a.MapPost("/otp", () => TypedResults.Ok()).RequireRateLimiting(PerClientRateLimits.Otp));
        var client = app.GetTestClient();
        for (var i = 0; i < 2; i++)
        {
            (await client.PostAsync("/otp", null)).Dispose();
        }

        using var rejected = await client.PostAsync("/otp", null);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType!.MediaType);
        Assert.True(rejected.Headers.RetryAfter is not null, "Retry-After header missing");
    }

    private static async Task<WebApplication> ClinicAppAsync() => await StartAsync(a =>
    {
        a.MapPost("/otp", () => TypedResults.Ok()).RequireRateLimiting(PerClientRateLimits.Otp);
        a.MapGet("/records", (Func<string>)(() => throw new InvalidOperationException("patient 1089234567 has no record")));
    });

    private static async Task<HttpResponseMessage> AsUser(HttpClient client, HttpMethod method, string path, string user)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("x-test-user", user);
        return await client.SendAsync(request);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_APatientAsksForACodeThreeTimes_TheThirdWaits_AnotherPatientIsServed_AndACrashShowsOnlyATraceId()
    {
        await using var app = await ClinicAppAsync();
        var client = app.GetTestClient();

        // Given: patient 7 has asked for a code twice
        (await AsUser(client, HttpMethod.Post, "/otp", "7")).Dispose();
        (await AsUser(client, HttpMethod.Post, "/otp", "7")).Dispose();

        // When: they ask a third time, patient 8 asks at the same moment, and the records page crashes for 8
        using var third = await AsUser(client, HttpMethod.Post, "/otp", "7");
        using var other = await AsUser(client, HttpMethod.Post, "/otp", "8");
        using var crash = await AsUser(client, HttpMethod.Get, "/records", "8");
        var crashBody = await crash.Content.ReadAsStringAsync();

        // Then: 7 is told to wait, with Retry-After; 8 is served; the crash gives support a traceId to search
        // for, and nothing about the patient
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.NotNull(third.Headers.RetryAfter);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, crash.StatusCode);
        Assert.Contains("traceId", crashBody);
        Assert.DoesNotContain("1089234567", crashBody);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public async Task Production_NoIcuNoTzdata_ProblemDetailsAndRateLimitsWork()
    {
        ProductionConditions.Require();
        await using var app = await ClinicAppAsync();
        var client = app.GetTestClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            using var response = await AsUser(client, HttpMethod.Post, "/otp", "7");
            statuses.Add(response.StatusCode);
        }

        using var crash = await AsUser(client, HttpMethod.Get, "/records", "7");

        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests }, statuses);
        Assert.Equal("application/problem+json", crash.Content.Headers.ContentType!.MediaType);
    }
}
