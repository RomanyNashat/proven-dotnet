using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SkillSamples.AuthServer;

public sealed record LoginRequest(string UserName, string Password);

public static class AuthServerEndpoints
{
    private const string OpenIddictScheme = OpenIddictServerAspNetCoreDefaults.AuthenticationScheme;

    public static IEndpointRouteBuilder MapAuthServer(this IEndpointRouteBuilder app)
    {
        // A real login page is a server-rendered form with antiforgery; the API shape keeps the sample short.
        app.MapPost("account/login", async (LoginRequest login, SignInManager<AppUser> signIn) =>
        {
            // lockoutOnFailure: true, or failed attempts are never counted and lockout never happens.
            var result = await signIn.PasswordSignInAsync(login.UserName, login.Password, isPersistent: false, lockoutOnFailure: true);
            // The same answer for a wrong password and a locked account: it tells an attacker nothing.
            return result.Succeeded ? Results.NoContent() : Results.Unauthorized();
        });

        app.MapMethods("connect/authorize", [HttpMethods.Get, HttpMethods.Post], Authorize);
        app.MapPost("connect/token", Token);
        app.MapMethods("connect/logout", [HttpMethods.Get, HttpMethods.Post], Logout);
        return app;
    }

    private static async Task<IResult> Authorize(HttpContext http, UserManager<AppUser> users, SignInManager<AppUser> signIn)
    {
        var request = http.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("Not an OpenIddict request.");
        var cookie = await http.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var user = cookie.Succeeded ? await users.GetUserAsync(cookie.Principal!) : null;
        if (user is null || !await signIn.CanSignInAsync(user))
        {
            // Not signed in (or locked since): to the login page, then back here.
            var returnUrl = http.Request.PathBase + http.Request.Path + http.Request.QueryString;
            return Results.Challenge(new AuthenticationProperties { RedirectUri = returnUrl }, [IdentityConstants.ApplicationScheme]);
        }

        var identity = await IdentityFor(user, users);
        identity.SetScopes(request.GetScopes());
        identity.SetResources(AuthServerSetup.OrdersApi);   // the services allowed to introspect it
        identity.SetDestinations(DestinationsOf);
        return Results.SignIn(new ClaimsPrincipal(identity), authenticationScheme: OpenIddictScheme);
    }

    private static async Task<IResult> Token(HttpContext http, UserManager<AppUser> users, SignInManager<AppUser> signIn)
    {
        var request = http.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("Not an OpenIddict request.");
        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
            return Refuse(Errors.UnsupportedGrantType, "Only the code and refresh token flows are allowed.");

        // The principal stored with the code or the refresh token. Without this handler OpenIddict issues it
        // as it was at sign-in, so a role removed since then would stay for the refresh token's lifetime.
        var stored = (await http.AuthenticateAsync(OpenIddictScheme)).Principal!;
        var user = await users.FindByIdAsync(stored.GetClaim(Claims.Subject)!);
        if (user is null || !await signIn.CanSignInAsync(user))
            return Refuse(Errors.InvalidGrant, "The user can no longer sign in.");

        var identity = await IdentityFor(user, users);
        identity.SetScopes(stored.GetScopes());
        identity.SetResources(stored.GetResources());
        identity.SetDestinations(DestinationsOf);
        return Results.SignIn(new ClaimsPrincipal(identity), authenticationScheme: OpenIddictScheme);
    }

    // Signs the user out and revokes every token issued to them, on every device. Ending only the
    // refresh token leaves the access token working until it expires.
    private static async Task<IResult> Logout(
        HttpContext http, UserManager<AppUser> users, SignInManager<AppUser> signIn, IOpenIddictTokenManager tokens)
    {
        var cookie = await http.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (cookie.Succeeded && await users.GetUserAsync(cookie.Principal!) is { } user)
        {
            await tokens.RevokeBySubjectAsync(await users.GetUserIdAsync(user));
            await signIn.SignOutAsync();
        }

        return Results.SignOut(authenticationSchemes: [OpenIddictScheme]);
    }

    private static async Task<ClaimsIdentity> IdentityFor(AppUser user, UserManager<AppUser> users)
    {
        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, await users.GetUserIdAsync(user))
            .SetClaim(Claims.Name, await users.GetUserNameAsync(user))
            .SetClaims(Claims.Role, [.. await users.GetRolesAsync(user)]);   // roles are rows, read every time
        return identity;
    }

    // Everything goes in the access token; name and roles also in the ID token when those scopes were granted.
    private static IEnumerable<string> DestinationsOf(Claim claim) => claim.Type switch
    {
        Claims.Name when claim.Subject!.HasScope(Scopes.Profile) => [OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken],
        Claims.Role when claim.Subject!.HasScope(Scopes.Roles) => [OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken],
        _ => [OpenIddictConstants.Destinations.AccessToken]
    };

    private static IResult Refuse(string error, string description) =>
        Results.Forbid(new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description
        }), [OpenIddictScheme]);
}
