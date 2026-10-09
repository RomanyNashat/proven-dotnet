using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
}
