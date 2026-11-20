using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace LabResults.Infrastructure;

/// <summary>JWT bearer settings for Keycloak access tokens.</summary>
public static class KeycloakJwt
{
    public const string Audience = "lab-results";

    public static void Configure(JwtBearerOptions o, string? authority, string audience)
    {
        if (string.IsNullOrWhiteSpace(authority))
        {
            throw new InvalidOperationException("Auth:Authority is not configured.");
        }

        o.Authority = authority;
        o.Audience = audience;
        o.RequireHttpsMetadata = true;

        // Keep the token's claim names ("sub", "preferred_username") instead of the legacy WS-* mapping.
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidAudience = audience,
            NameClaimType = "preferred_username",
            RoleClaimType = "roles",
        };

        var previous = o.Events.OnTokenValidated;
        o.Events.OnTokenValidated = async context =>
        {
            await previous(context);
            if (context.Principal?.Identity is ClaimsIdentity identity)
            {
                AddKeycloakRoles(identity, audience);
            }
        };
    }

    /// <summary>
    /// Keycloak puts realm roles in <c>realm_access.roles</c> and client roles in <c>resource_access.{client}.roles</c>,
    /// both nested JSON objects, which ASP.NET does not turn into role claims on its own.
    /// </summary>
    public static void AddKeycloakRoles(ClaimsIdentity identity, string clientId)
    {
        var roles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var claim in identity.FindAll("realm_access"))
        {
            ReadRoles(claim.Value, null, roles);
        }

        foreach (var claim in identity.FindAll("resource_access"))
        {
            ReadRoles(claim.Value, clientId, roles);
        }

        foreach (var role in roles)
        {
            if (!identity.HasClaim(identity.RoleClaimType, role))
            {
                identity.AddClaim(new Claim(identity.RoleClaimType, role));
            }
        }
    }

    private static void ReadRoles(string json, string? clientId, HashSet<string> roles)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var element = doc.RootElement;
            if (clientId is not null)
            {
                if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(clientId, out element))
                {
                    return;
                }
            }

            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty("roles", out var list)
                && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var role in list.EnumerateArray())
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
            // Not a Keycloak access claim; grants nothing.
        }
    }
}
