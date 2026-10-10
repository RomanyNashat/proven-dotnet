using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using SkillSamples.Postgres;
using Xunit;

namespace SkillSamples.Jobs;

/// <summary>X-Role: none → anonymous; any value → a user with that role.</summary>
public sealed class RoleHeaderAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Role", out var role))
            return Task.FromResult(AuthenticateResult.NoResult());
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "tester"), new Claim(ClaimTypes.Role, role.ToString())], "Header");
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Header")));
    }
}

[Collection("hangfire")]
public sealed class HangfireDashboardTests(PgDatabase db) : IClassFixture<PgDatabase>
{
    [Fact]
    public async Task Dashboard_OnlyTheJobsAdminPolicyGetsIn()
    {
        await using (var connection = new NpgsqlConnection(db.ConnectionString))
        {
            await connection.OpenAsync();
            PostgreSqlObjectsInstaller.Install(connection, "hangfire");
        }

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddJobStorage(db.ConnectionString);
        builder.Services.AddAuthentication("Header").AddScheme<AuthenticationSchemeOptions, RoleHeaderAuth>("Header", null);
        builder.Services.AddAuthorization(o => o.AddPolicy(HangfireDashboard.Policy, p => p.RequireRole("jobs-admin")));
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapJobsDashboard();
        await app.StartAsync();

        var client = app.GetTestClient();
        async Task<HttpStatusCode> Get(string? role)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/hangfire");
            if (role is not null)
                request.Headers.Add("X-Role", role);
            using var response = await client.SendAsync(request);
            return response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.Unauthorized, await Get(null));
        Assert.Equal(HttpStatusCode.Forbidden, await Get("support"));
        Assert.Equal(HttpStatusCode.OK, await Get("jobs-admin"));

        await app.StopAsync();
    }
}
