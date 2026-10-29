using System.Security.Claims;
using System.Text.Json;

namespace LabResults.Infrastructure;

/// <summary>
/// Keycloak puts realm roles in a nested object, <c>"realm_access": { "roles": [...] }</c>, which arrives as one JSON
/// claim. ASP.NET Core can't read roles from inside it, so they are copied out as role claims.
/// </summary>
public static class KeycloakRoles
{
    /// <summary>A claim type no token carries, so only roles copied from <c>realm_access</c> count.</summary>
    public const string RoleClaimType = ClaimTypes.Role;

    private const string RealmAccess = "realm_access";

    /// <summary>Adds a role claim for each role in the identity's <c>realm_access.roles</c>; ignores a malformed value.</summary>
    /// <param name="identity">The identity built from a validated token.</param>
    public static void AddRealmRoles(ClaimsIdentity identity)
    {
        var claim = identity.FindFirst(RealmAccess);
        if (claim is null)
        {
            return;
        }

        try
        {
            using var json = JsonDocument.Parse(claim.Value);
            if (json.RootElement.ValueKind != JsonValueKind.Object
                || !json.RootElement.TryGetProperty("roles", out var roles)
                || roles.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var role in roles.EnumerateArray())
            {
                if (role.ValueKind == JsonValueKind.String && role.GetString() is { Length: > 0 } name)
                {
                    identity.AddClaim(new Claim(RoleClaimType, name, ClaimValueTypes.String, claim.Issuer));
                }
            }
        }
        catch (JsonException)
        {
            // A token without readable realm roles simply has no roles.
        }
    }
}
