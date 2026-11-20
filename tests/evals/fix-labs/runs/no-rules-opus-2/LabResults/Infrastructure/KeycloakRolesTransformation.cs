using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

namespace LabResults.Infrastructure;

/// <summary>
/// Keycloak puts roles in nested JSON (<c>realm_access.roles</c>, <c>resource_access.{client}.roles</c>), which the
/// JWT handler surfaces as a single JSON-valued claim. Flatten them into role claims so <c>RequireRole</c> works.
/// </summary>
public sealed class KeycloakRolesTransformation : IClaimsTransformation
{
    public const string ClientId = "lab-results";

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity { IsAuthenticated: true })
        {
            return Task.FromResult(principal);
        }

        var roles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var claim in principal.FindAll("realm_access"))
        {
            AddRoles(claim.Value, roles, null);
        }

        foreach (var claim in principal.FindAll("resource_access"))
        {
            AddRoles(claim.Value, roles, ClientId);
        }

        if (roles.Count == 0)
        {
            return Task.FromResult(principal);
        }

        var clone = principal.Clone();
        var identity = (ClaimsIdentity)clone.Identity!;
        foreach (var role in roles)
        {
            if (!identity.HasClaim(identity.RoleClaimType, role))
            {
                identity.AddClaim(new Claim(identity.RoleClaimType, role));
            }
        }

        return Task.FromResult(clone);
    }

    private static void AddRoles(string json, HashSet<string> roles, string? client)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var element = doc.RootElement;
            if (client is not null)
            {
                if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(client, out element))
                {
                    return;
                }
            }

            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty("roles", out var array)
                && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var role in array.EnumerateArray())
                {
                    if (role.ValueKind == JsonValueKind.String && role.GetString() is { Length: > 0 } name)
                    {
                        roles.Add(name);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Not the shape we expect: grant nothing.
        }
    }
}
