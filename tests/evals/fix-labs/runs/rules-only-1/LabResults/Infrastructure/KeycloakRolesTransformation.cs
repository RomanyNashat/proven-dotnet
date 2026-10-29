using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

namespace LabResults.Infrastructure;

/// <summary>
/// Keycloak puts realm roles in a nested claim, <c>"realm_access": { "roles": [...] }</c>, which ASP.NET Core
/// does not read as roles. This flattens them into the identity's role claim type so <c>RequireRole</c> works.
/// </summary>
public sealed class KeycloakRolesTransformation : IClaimsTransformation
{
    private const string RealmAccessClaim = "realm_access";

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        foreach (var identity in principal.Identities.Where(i => i.IsAuthenticated))
        {
            foreach (var role in RealmRoles(identity))
            {
                if (!identity.HasClaim(identity.RoleClaimType, role))
                {
                    identity.AddClaim(new Claim(identity.RoleClaimType, role, ClaimValueTypes.String, identity.FindFirst(RealmAccessClaim)?.Issuer));
                }
            }
        }

        return Task.FromResult(principal);
    }

    private static List<string> RealmRoles(ClaimsIdentity identity)
    {
        var roles = new List<string>();
        foreach (var claim in identity.FindAll(RealmAccessClaim))
        {
            try
            {
                using var document = JsonDocument.Parse(claim.Value);
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("roles", out var array)
                    && array.ValueKind == JsonValueKind.Array)
                {
                    roles.AddRange(array.EnumerateArray()
                        .Where(r => r.ValueKind == JsonValueKind.String)
                        .Select(r => r.GetString()!));
                }
            }
            catch (JsonException)
            {
                // A malformed claim grants no roles; the request then fails authorization.
            }
        }

        return roles;
    }
}
