using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SkillSamples.AuthServer;

// Run as a deploy step, like the reviewed migration script; safe to run again. Roles are rows: adding
// one later is an insert, not a code change. The admin's password comes from the secret store.
public static class AuthServerSeed
{
    public const string SuperAdmin = "super-admin";

    public static async Task SeedAsync(IServiceProvider services, string adminEmail, string adminPassword, Uri portalCallback, string ordersApiSecret)
    {
        await using var scope = services.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var apps = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        if (!await roles.RoleExistsAsync(SuperAdmin))
            Check(await roles.CreateAsync(new AppRole(SuperAdmin)));

        if (await users.FindByEmailAsync(adminEmail) is null)
        {
            var admin = new AppUser { UserName = adminEmail, Email = adminEmail, LockoutEnabled = true };
            Check(await users.CreateAsync(admin, adminPassword));
            Check(await users.AddToRoleAsync(admin, SuperAdmin));
        }

        // The portal: a public client (a browser app can't keep a secret), so PKCE is required.
        if (await apps.FindByClientIdAsync("admin-portal") is null)
        {
            await apps.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = "admin-portal",
                ClientType = ClientTypes.Public,
                RedirectUris = { portalCallback },
                PostLogoutRedirectUris = { new Uri(portalCallback, "/") },
                Permissions =
                {
                    Permissions.Endpoints.Authorization, Permissions.Endpoints.Token,
                    Permissions.Endpoints.Revocation, Permissions.Endpoints.EndSession,
                    Permissions.GrantTypes.AuthorizationCode, Permissions.GrantTypes.RefreshToken,
                    Permissions.ResponseTypes.Code,
                    Permissions.Scopes.Profile, Permissions.Scopes.Roles, Permissions.Prefixes.Scope + "orders"
                },
                Requirements = { Requirements.Features.ProofKeyForCodeExchange }
            });
        }

        // A service that checks tokens: a confidential client allowed only to introspect.
        if (await apps.FindByClientIdAsync(AuthServerSetup.OrdersApi) is null)
        {
            await apps.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = AuthServerSetup.OrdersApi,
                ClientSecret = ordersApiSecret,
                ClientType = ClientTypes.Confidential,
                Permissions = { Permissions.Endpoints.Introspection }
            });
        }
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}
