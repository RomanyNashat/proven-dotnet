using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SkillSamples.MsSql;
using Xunit;

namespace SkillSamples.Auth;

/// <summary>Cases 2 and 3 of auth-patterns: tokens from your own OpenIddict server, JWT and reference.</summary>
public sealed class PortalAuthTests(SqlDatabase db) : IClassFixture<SqlDatabase>
{
    private sealed record TokenResponse([property: JsonPropertyName("access_token")] string AccessToken);

    private static async Task<string> TokenFor(WebApplication server, string clientId)
    {
        using var http = new HttpClient { BaseAddress = PortalServer.Address(server) };
        using var response = await http.PostAsync("connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials", ["client_id"] = clientId, ["client_secret"] = PortalServer.Secret, ["scope"] = "visits",
        }));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        return (await response.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken;
    }

    private static async Task RevokeAsync(WebApplication server, string clientId, string token)
    {
        using var http = new HttpClient { BaseAddress = PortalServer.Address(server) };
        using var response = await http.PostAsync("connect/revoke", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = token, ["client_id"] = clientId, ["client_secret"] = PortalServer.Secret,
        }));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<WebApplication> StartApiAsync(WebApplication server, bool introspection)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Authority"] = PortalServer.Address(server).ToString(),
            ["Auth:Audience"] = "visits-api",
            ["Auth:ClientId"] = "visits-api",
            ["Auth:ClientSecret"] = PortalServer.Secret,
        });
        if (introspection)
        {
            builder.Services.AddPortalIntrospection(builder.Configuration);
        }
        else
        {
            builder.Services.AddPortalJwt(builder.Configuration);
            builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o => o.RequireHttpsMetadata = false);   // the test server is plain HTTP
        }

        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy("AdminOnly", p => p.RequireRole("admin"));
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/me", (ClaimsPrincipal user) => user.FindFirstValue("sub"));
        app.MapGet("/admin", () => "admin area").RequireAuthorization("AdminOnly");
        await app.StartAsync();
        return app;
    }

    private static async Task<(HttpStatusCode Status, string Body)> GetAsync(WebApplication api, string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await api.GetTestClient().SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Jwt_EncryptedAsOpenIddictDefaults_JwtBearerRejectsEveryToken()
    {
        await using var server = await PortalServer.StartAsync(db.ConnectionString, encryptAccessTokens: true, referenceTokens: false);
        await using var api = await StartApiAsync(server, introspection: false);

        var (status, _) = await GetAsync(api, "/me", await TokenFor(server, "admin-tool"));

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    [Fact]
    public async Task Jwt_WithEncryptionOff_IsValidatedLikeKeycloaks_RolesIncluded()
    {
        await using var server = await PortalServer.StartAsync(db.ConnectionString, encryptAccessTokens: false, referenceTokens: false);
        await using var api = await StartApiAsync(server, introspection: false);

        Assert.Equal((HttpStatusCode.OK, "admin-tool"), await GetAsync(api, "/me", await TokenFor(server, "admin-tool")));
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(api, "/admin", await TokenFor(server, "admin-tool"))).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(api, "/admin", await TokenFor(server, "reports-job"))).Status);
    }

    [Fact]
    public async Task Jwt_AfterRevocation_IsStillAcceptedUntilItExpires()
    {
        await using var server = await PortalServer.StartAsync(db.ConnectionString, encryptAccessTokens: false, referenceTokens: false);
        await using var api = await StartApiAsync(server, introspection: false);
        var token = await TokenFor(server, "admin-tool");

        await RevokeAsync(server, "admin-tool", token);

        Assert.Equal(HttpStatusCode.OK, (await GetAsync(api, "/me", token)).Status);   // the service never asks the server
    }

    [Fact]
    public async Task Reference_IsIntrospected_RolesIncluded_AndRevocationIsImmediate()
    {
        await using var server = await PortalServer.StartAsync(db.ConnectionString, encryptAccessTokens: true, referenceTokens: true);
        await using var api = await StartApiAsync(server, introspection: true);
        var admin = await TokenFor(server, "admin-tool");

        Assert.Equal((HttpStatusCode.OK, "admin-tool"), await GetAsync(api, "/me", admin));
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(api, "/admin", admin)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(api, "/admin", await TokenFor(server, "reports-job"))).Status);

        await RevokeAsync(server, "admin-tool", admin);

        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(api, "/me", admin)).Status);
    }

    [Fact]
    public async Task Reference_WhenTheServerIsDown_TheRequestIsRefused()
    {
        var server = await PortalServer.StartAsync(db.ConnectionString, encryptAccessTokens: true, referenceTokens: true);
        await using var api = await StartApiAsync(server, introspection: true);
        var token = await TokenFor(server, "admin-tool");
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(api, "/me", token)).Status);

        await server.DisposeAsync();
        (HttpStatusCode Status, string Body)? answer = null;
        var error = await Record.ExceptionAsync(async () => answer = await GetAsync(api, "/me", token));

        // Refused either way: a 401, or the introspection failure surfacing as an error. Never the user.
        Assert.True(error is not null || answer!.Value.Status != HttpStatusCode.OK, $"{answer?.Status}: {answer?.Body}");
        Assert.NotEqual("admin-tool", answer?.Body);
    }
}
