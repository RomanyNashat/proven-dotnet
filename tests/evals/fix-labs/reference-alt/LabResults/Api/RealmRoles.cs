using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

namespace LabResults.Api;

/// <summary>Adds Keycloak's realm roles (inside the realm_access JSON claim) as role claims.</summary>
public sealed class RealmRoles : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is ClaimsIdentity { IsAuthenticated: true } identity
            && !identity.HasClaim(c => c.Type == "roles_mapped")
            && identity.FindFirst("realm_access")?.Value is { } json)
        {
            var roles = JsonSerializer.Deserialize<RealmAccess>(json)?.roles ?? [];
            foreach (var role in roles)
            {
                identity.AddClaim(new Claim(ClaimTypes.Role, role));
            }

            identity.AddClaim(new Claim("roles_mapped", "true"));
        }

        return Task.FromResult(principal);
    }

    private sealed record RealmAccess(string[]? roles);
}
