using System.Security.Claims;
using System.Text.Json;

namespace LabResults.Infrastructure;

/// <summary>
/// Keycloak nests roles inside JSON claims (<c>realm_access.roles</c>, <c>resource_access.{client}.roles</c>);
/// no claim is literally named "realm_access.roles", so role checks need them copied out as flat claims.
/// </summary>
public static class KeycloakRoles
{
    public const string RoleClaimType = "roles";

    /// <summary>
    /// Adds the realm roles and this client's roles as claims of the identity's own role claim type, so
    /// <c>IsInRole</c> sees them whatever <c>RoleClaimType</c> the token validation was set up with.
    /// </summary>
    /// <param name="identity">The validated token's identity.</param>
    /// <param name="clientId">This service's Keycloak client, whose client roles count.</param>
    public static void AddRoles(ClaimsIdentity identity, string clientId)
    {
        var roles = new HashSet<string>(StringComparer.Ordinal);
        if (Parse(identity.FindFirst("realm_access")?.Value) is { } realm)
        {
            AddFrom(realm, roles);
        }

        if (Parse(identity.FindFirst("resource_access")?.Value) is { } resources
            && resources.ValueKind == JsonValueKind.Object
            && resources.TryGetProperty(clientId, out var client))
        {
            AddFrom(client, roles);
        }

        foreach (var role in roles)
        {
            identity.AddClaim(new Claim(identity.RoleClaimType, role));
        }
    }

    private static JsonElement? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            // A malformed claim grants no roles; the request then fails authorization, which is the safe outcome.
            return null;
        }
    }

    private static void AddFrom(JsonElement access, HashSet<string> roles)
    {
        if (access.ValueKind != JsonValueKind.Object
            || !access.TryGetProperty("roles", out var list)
            || list.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var role in list.EnumerateArray())
        {
            if (role.ValueKind == JsonValueKind.String && role.GetString() is { Length: > 0 } name)
            {
                roles.Add(name);
            }
        }
    }
}
