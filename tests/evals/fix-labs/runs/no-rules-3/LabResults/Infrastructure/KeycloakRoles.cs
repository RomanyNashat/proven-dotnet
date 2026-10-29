using System.Security.Claims;
using System.Text.Json;

namespace LabResults.Infrastructure;

public static class KeycloakRoles
{
    /// <summary>Copies <c>realm_access.roles</c> from the token into <see cref="ClaimTypes.Role"/> claims.</summary>
    public static void Flatten(ClaimsPrincipal? principal)
    {
        if (principal?.Identity is not ClaimsIdentity identity)
        {
            return;
        }

        foreach (var claim in identity.FindAll("realm_access").ToList())
        {
            try
            {
                using var doc = JsonDocument.Parse(claim.Value);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("roles", out var roles)
                    && roles.ValueKind == JsonValueKind.Array)
                {
                    foreach (var role in roles.EnumerateArray())
                    {
                        if (role.ValueKind == JsonValueKind.String)
                        {
                            identity.AddClaim(new Claim(ClaimTypes.Role, role.GetString()!));
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // A malformed claim grants nothing.
            }
        }
    }
}
