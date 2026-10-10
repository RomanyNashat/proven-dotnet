using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using SkillSamples.MsSql;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.AuthServer;

public sealed class AuthServerTests(SqlDatabase db) : IClassFixture<SqlDatabase>, IAsyncLifetime
{
    private const string Admin = "admin@example.com";
    private const string Password = "correct-horse-battery-1";
    private const string OrdersSecret = "orders-api-secret-for-tests";
    private static readonly Uri Callback = new("https://portal.example.com/callback");

    private WebApplication _server = null!;
    private readonly ErrorLog _errors = new();

    private sealed record Tokens(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string RefreshToken,
        [property: JsonPropertyName("id_token")] string IdToken);

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0));
        builder.Logging.ClearProviders().AddProvider(_errors);
        builder.Services.AddAuthServer(db.ConnectionString, o =>
        {
            o.AddEphemeralEncryptionKey().AddEphemeralSigningKey();
            o.UseAspNetCore().DisableTransportSecurityRequirement();   // the tests talk plain http
        });
        _server = builder.Build();
        _server.MapAuthServer();
        await CreateSchemaAsync();
        await AuthServerSeed.SeedAsync(_server.Services, Admin, Password, Callback, OrdersSecret);
        await _server.StartAsync();
    }

    public async Task DisposeAsync() => await _server.DisposeAsync();

    // Stands in for the reviewed migration script.
    private async Task CreateSchemaAsync()
    {
        await using var scope = _server.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        if (await db.ScalarAsync<int>("SELECT COUNT(*) FROM sys.tables WHERE name = 'OpenIddictTokens'") > 0)
            return;
        foreach (var batch in Regex.Split(context.Database.GenerateCreateScript(), @"^\s*GO\s*$", RegexOptions.Multiline))
        {
            if (!string.IsNullOrWhiteSpace(batch))
                await context.Database.ExecuteSqlRawAsync(batch);
        }
    }

    private async Task<T> WithUsers<T>(Func<UserManager<AppUser>, RoleManager<AppRole>, Task<T>> work)
    {
        await using var scope = _server.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(),
            scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>());
    }

    private Task<bool> CreateUserAsync(string email, params string[] roles) => WithUsers(async (users, roleManager) =>
    {
        var user = new AppUser { UserName = email, Email = email, LockoutEnabled = true };
        Assert.True((await users.CreateAsync(user, Password)).Succeeded);
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new AppRole(role));
            await users.AddToRoleAsync(user, role);
        }
        return true;
    });

    private HttpClient Browser() =>
        new(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() })
        {
            BaseAddress = new Uri(_server.Urls.First() + "/")
        };

    private static async Task<HttpStatusCode> LoginAsync(HttpClient browser, string user, string password)
    {
        using var response = await browser.PostAsJsonAsync("account/login", new LoginRequest(user, password));
        return response.StatusCode;
    }

    private static (string Verifier, string Challenge) Pkce()
    {
        var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        return (verifier, Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    private static async Task<HttpResponseMessage> AuthorizeAsync(HttpClient browser, string challenge) =>
        await browser.GetAsync(QueryHelpers.AddQueryString("connect/authorize", new Dictionary<string, string?>
        {
            ["client_id"] = "admin-portal",
            ["response_type"] = "code",
            ["redirect_uri"] = Callback.ToString(),
            ["scope"] = "openid profile roles offline_access orders",
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = "state-1"
        }));

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string path, Dictionary<string, string> form) =>
        await client.PostAsync(path, new FormUrlEncodedContent(form));

    // Signs in, runs the code flow with PKCE, and returns the browser (with its cookie) and the tokens.
    private async Task<(HttpClient Browser, Tokens Tokens)> SignInAsync(string user, string password = Password)
    {
        var browser = Browser();
        Assert.Equal(HttpStatusCode.NoContent, await LoginAsync(browser, user, password));
        var (verifier, challenge) = Pkce();
        using var authorize = await AuthorizeAsync(browser, challenge);
        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        var code = QueryHelpers.ParseQuery(authorize.Headers.Location!.Query)["code"].ToString();

        using var token = await PostFormAsync(browser, "connect/token", new()
        {
            ["grant_type"] = "authorization_code", ["client_id"] = "admin-portal", ["code"] = code,
            ["redirect_uri"] = Callback.ToString(), ["code_verifier"] = verifier
        });
        Assert.True(token.IsSuccessStatusCode, await token.Content.ReadAsStringAsync());
        return (browser, (await token.Content.ReadFromJsonAsync<Tokens>())!);
    }

    // What the orders API does with each request's token.
    private async Task<JsonElement> IntrospectAsync(string token)
    {
        using var api = new HttpClient { BaseAddress = new Uri(_server.Urls.First() + "/") };
        using var response = await PostFormAsync(api, "connect/introspect", new()
        {
            ["token"] = token, ["client_id"] = AuthServerSetup.OrdersApi, ["client_secret"] = OrdersSecret
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    }

    private static string[] RolesIn(JsonElement introspection) =>
        !introspection.TryGetProperty("role", out var role) ? []
        : role.ValueKind == JsonValueKind.Array ? role.EnumerateArray().Select(r => r.GetString()!).ToArray()
        : [role.GetString()!];

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_AnAdminSignsInThroughThePortal_TheOrdersApiSeesTheirRole()
    {
        var (browser, tokens) = await SignInAsync(Admin);
        using var _ = browser;

        var introspection = await IntrospectAsync(tokens.AccessToken);

        Assert.True(introspection.GetProperty("active").GetBoolean());
        Assert.Equal(Admin, introspection.GetProperty("name").GetString());
        Assert.Equal(new[] { AuthServerSeed.SuperAdmin }, RolesIn(introspection));
        Assert.False(string.IsNullOrEmpty(tokens.IdToken));
        Assert.DoesNotContain('.', tokens.AccessToken);   // a reference token, not a JWT
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_NotSignedIn_TheAuthorizeRequestGoesToTheLoginPage()
    {
        using var browser = Browser();

        using var response = await AuthorizeAsync(browser, Pkce().Challenge);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_SomeoneInterceptsTheCodeButNotTheVerifier_TheTokenRequestIsRefused()
    {
        using var browser = Browser();
        Assert.Equal(HttpStatusCode.NoContent, await LoginAsync(browser, Admin, Password));
        using var authorize = await AuthorizeAsync(browser, Pkce().Challenge);
        var code = QueryHelpers.ParseQuery(authorize.Headers.Location!.Query)["code"].ToString();

        using var attacker = new HttpClient { BaseAddress = browser.BaseAddress };
        using var stolen = await PostFormAsync(attacker, "connect/token", new()
        {
            ["grant_type"] = "authorization_code", ["client_id"] = "admin-portal", ["code"] = code,
            ["redirect_uri"] = Callback.ToString(), ["code_verifier"] = Pkce().Verifier
        });

        Assert.Equal(HttpStatusCode.BadRequest, stolen.StatusCode);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_FiveWrongPasswords_EvenTheRightOneIsRefusedAfter()
    {
        const string user = "guessed@example.com";
        await CreateUserAsync(user);
        using var browser = Browser();

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, await LoginAsync(browser, user, $"wrong-password-{i}"));

        Assert.Equal(HttpStatusCode.Unauthorized, await LoginAsync(browser, user, Password));
        Assert.True(await WithUsers(async (users, _) => await users.IsLockedOutAsync((await users.FindByEmailAsync(user))!)));
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_AnAdminLosesTheirRole_TheNextRefreshDropsIt()
    {
        const string user = "moved-team@example.com";
        await CreateUserAsync(user, "orders-admin");
        var (browser, tokens) = await SignInAsync(user);
        using var _ = browser;
        Assert.Equal(new[] { "orders-admin" }, RolesIn(await IntrospectAsync(tokens.AccessToken)));

        // When: the role is removed (a row), and the portal refreshes its token
        await WithUsers(async (users, _) => (await users.RemoveFromRoleAsync((await users.FindByEmailAsync(user))!, "orders-admin")).Succeeded);
        using var refresh = await PostFormAsync(browser, "connect/token", new()
        {
            ["grant_type"] = "refresh_token", ["client_id"] = "admin-portal", ["refresh_token"] = tokens.RefreshToken
        });
        var refreshed = (await refresh.Content.ReadFromJsonAsync<Tokens>())!;

        // Then: the new token has no role
        Assert.Empty(RolesIn(await IntrospectAsync(refreshed.AccessToken)));
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_RevokingOnlyTheRefreshToken_LeavesTheAccessTokenWorking()
    {
        var (browser, tokens) = await SignInAsync(Admin);
        using var _ = browser;

        using var revoke = await PostFormAsync(browser, "connect/revoke", new()
        {
            ["token"] = tokens.RefreshToken, ["client_id"] = "admin-portal"
        });

        Assert.True(revoke.IsSuccessStatusCode);
        Assert.True((await IntrospectAsync(tokens.AccessToken)).GetProperty("active").GetBoolean());
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_TheAdminLogsOut_EveryTokenStopsAtOnce()
    {
        var (browser, tokens) = await SignInAsync(Admin);
        using var _ = browser;

        using var logout = await browser.GetAsync(QueryHelpers.AddQueryString("connect/logout", new Dictionary<string, string?>
        {
            ["client_id"] = "admin-portal", ["post_logout_redirect_uri"] = new Uri(Callback, "/").ToString()
        }));

        Assert.True(logout.StatusCode == HttpStatusCode.Redirect, $"{logout.StatusCode}: {_errors}");
        Assert.False((await IntrospectAsync(tokens.AccessToken)).GetProperty("active").GetBoolean());
        using var refresh = await PostFormAsync(browser, "connect/token", new()
        {
            ["grant_type"] = "refresh_token", ["client_id"] = "admin-portal", ["refresh_token"] = tokens.RefreshToken
        });
        Assert.Equal(HttpStatusCode.BadRequest, refresh.StatusCode);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_TheDbaReviewsTheSchema_OnlyOpenIddictsOwnColumnsAreUnbounded()
    {
        await using var connection = await db.OpenAsync();
        var unbounded = (await connection.QueryAsync<string>(
            "SELECT TABLE_NAME + '.' + COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE CHARACTER_MAXIMUM_LENGTH = -1")).ToList();
        var binary = await db.ScalarAsync<int>("SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE DATA_TYPE IN ('varbinary', 'binary', 'image')");

        Assert.NotEmpty(unbounded);
        Assert.All(unbounded, column => Assert.StartsWith("OpenIddict", column));
        Assert.Contains("OpenIddictTokens.Payload", unbounded);
        Assert.Equal(0, binary);
        Assert.Equal("bigint", await db.ScalarAsync<string>("SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'OpenIddictTokens' AND COLUMN_NAME = 'Id'"));
        Assert.Equal("int", await db.ScalarAsync<string>("SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'AspNetUsers' AND COLUMN_NAME = 'Id'"));
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_TheAdminTypesTheirEmailInAnotherCase_TheyStillSignIn()
    {
        var (browser, tokens) = await SignInAsync("Admin@Example.COM");
        using var _ = browser;

        var introspection = await IntrospectAsync(tokens.AccessToken);
        Assert.True(introspection.GetProperty("active").GetBoolean());
        Assert.Equal(Admin, introspection.GetProperty("name").GetString());
    }
}
