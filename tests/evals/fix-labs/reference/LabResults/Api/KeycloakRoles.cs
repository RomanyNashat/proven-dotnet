using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace LabResults.Api;

/// <summary>Keycloak's realm roles arrive as <c>"realm_access": { "roles": [...] }</c>; this makes each one a role claim.</summary>
public static class KeycloakRoles
{
    public const string ClaimType = "role";

    public static Task MapAsync(TokenValidatedContext context)
    {
        if (context.Principal?.Identity is ClaimsIdentity identity && identity.FindFirst("realm_access")?.Value is { } json)
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("roles", out var roles) && roles.ValueKind == JsonValueKind.Array)
            {
                foreach (var role in roles.EnumerateArray())
                {
                    if (role.GetString() is { } name)
                    {
                        identity.AddClaim(new Claim(ClaimType, name));
                    }
                }
            }
        }

        return Task.CompletedTask;
    }
}
