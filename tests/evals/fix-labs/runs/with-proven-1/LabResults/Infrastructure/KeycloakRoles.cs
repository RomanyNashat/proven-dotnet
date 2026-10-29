using System.Security.Claims;
using System.Text.Json;

namespace LabResults.Infrastructure;

/// <summary>
/// Keycloak puts roles inside JSON claims (<c>realm_access.roles</c>, <c>resource_access.&lt;client&gt;.roles</c>).
/// <c>RequireRole</c> needs one claim per role, so they're copied out as <see cref="RoleClaim"/> claims.
/// </summary>
public static class KeycloakRoles
{
    public const string RoleClaim = "roles";

    /// <summary>Adds a <see cref="RoleClaim"/> claim for each realm role and each role of <paramref name="client"/>.</summary>
    /// <param name="identity">The validated token's identity.</param>
    /// <param name="client">The Keycloak client whose client roles count, or null for realm roles only.</param>
    public static void AddTo(ClaimsIdentity identity, string? client)
    {
        var roles = RolesIn(identity.FindFirst("realm_access")?.Value, "roles");
        if (client is not null)
        {
            roles = roles.Concat(RolesIn(identity.FindFirst("resource_access")?.Value, client, "roles"));
        }

        foreach (var role in roles.Distinct(StringComparer.Ordinal).ToList())
        {
            identity.AddClaim(new Claim(RoleClaim, role));
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
            // A signed token with a malformed claim gets no roles: fail closed (403), not a 500.
            return [];
        }
    }
}
