using System.Security.Claims;
using System.Text.Json;

namespace LabResults.Infrastructure;

/// <summary>
/// Keycloak puts roles inside JSON claims ("realm_access": { "roles": [...] } and
/// "resource_access": { "&lt;client&gt;": { "roles": [...] } }). RequireRole sees none of them until they
/// are copied out as one claim per role.
/// </summary>
public static class KeycloakRoles
{
    /// <summary>Adds a role claim, of the identity's own role claim type, for each Keycloak role.</summary>
    /// <param name="identity">The validated token's identity.</param>
    /// <param name="client">The Keycloak client whose client roles also count.</param>
    public static void AddRoleClaims(ClaimsIdentity identity, string client)
    {
        var roles = RolesIn(identity.FindFirst("realm_access")?.Value, "roles")
            .Concat(RolesIn(identity.FindFirst("resource_access")?.Value, client, "roles"))
            .Distinct(StringComparer.Ordinal)
            .Where(role => !identity.HasClaim(identity.RoleClaimType, role))
            .ToList();

        foreach (var role in roles)
        {
            identity.AddClaim(new Claim(identity.RoleClaimType, role));
        }
    }

    private static IEnumerable<string> RolesIn(string? json, params string[] path)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var element = document.RootElement;
            foreach (var name in path)
            {
                if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out element))
                {
                    return [];
                }
            }

            return element.ValueKind == JsonValueKind.Array
                ? [.. element.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.String).Select(r => r.GetString()!)]
                : [];
        }
        catch (JsonException)
        {
            return [];   // a malformed claim grants no role: fail closed
        }
    }
}
