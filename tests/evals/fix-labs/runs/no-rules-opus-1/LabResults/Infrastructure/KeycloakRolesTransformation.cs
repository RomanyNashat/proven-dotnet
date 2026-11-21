using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

namespace LabResults.Infrastructure;

/// <summary>
/// Keycloak puts realm roles in a nested claim, <c>"realm_access": { "roles": [...] }</c>, which ASP.NET Core
/// cannot address with a <c>RoleClaimType</c>. This flattens them into a separate identity whose role claim
/// type is <see cref="ClaimTypes.Role"/>, so <c>RequireRole</c> works regardless of how the JWT handler is configured.
/// </summary>
public sealed class KeycloakRolesTransformation : IClaimsTransformation
{
    public const string AuthenticationType = "keycloak-realm-roles";

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        // Claims transformation can run more than once per request; only add the roles once.
        if (principal.Identity?.IsAuthenticated != true || principal.Identities.Any(i => i.AuthenticationType == AuthenticationType))
        {
            return Task.FromResult(principal);
        }

        var roles = new ClaimsIdentity(AuthenticationType, nameType: null, roleType: ClaimTypes.Role);
        foreach (var role in RealmRoles(principal))
        {
            roles.AddClaim(new Claim(ClaimTypes.Role, role));
        }

        principal.AddIdentity(roles);
        return Task.FromResult(principal);
    }

    public static IEnumerable<string> RealmRoles(ClaimsPrincipal principal)
    {
        foreach (var claim in principal.FindAll("realm_access"))
        {
            List<string> found = [];
            try
            {
                using var doc = JsonDocument.Parse(claim.Value);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("roles", out var array)
                    && array.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in array.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } role)
                        {
                            found.Add(role);
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Malformed claim: grant nothing.
            }

            foreach (var role in found)
            {
                yield return role;
            }
        }
    }
}
