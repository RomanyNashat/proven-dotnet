using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace SkillSamples.Auth;

public static class KeycloakJwt
{
    public const string RoleClaim = "roles";

    public static AuthenticationBuilder AddKeycloakJwt(this IServiceCollection services, IConfiguration config) =>
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = config["Auth:Authority"];   // https://<keycloak>/realms/<realm>
                options.Audience = config["Auth:Audience"];     // Keycloak sends "account" unless the client has an audience mapper
                options.RequireHttpsMetadata = true;
                options.MapInboundClaims = false;               // keep "sub", "preferred_username" as they are
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "preferred_username",
                    RoleClaimType = RoleClaim,
                };
                options.Events = new JwtBearerEvents { OnTokenValidated = context => AddKeycloakRoles(context, config["Auth:Audience"]) };
            });

    /// <summary>
    /// Keycloak puts roles inside JSON claims: "realm_access": { "roles": [...] } and
    /// "resource_access": { "&lt;client&gt;": { "roles": [...] } }. ASP.NET Core needs one claim per role, so
    /// RequireRole and IsInRole see nothing until they're copied out.
    /// </summary>
    private static Task AddKeycloakRoles(TokenValidatedContext context, string? client)
    {
        if (context.Principal?.Identity is not ClaimsIdentity identity)
        {
            return Task.CompletedTask;
        }

        var roles = RolesIn(identity.FindFirst("realm_access")?.Value, "roles");
        if (client is not null)
        {
            roles = roles.Concat(RolesIn(identity.FindFirst("resource_access")?.Value, client, "roles"));
        }

        foreach (var role in roles.Distinct(StringComparer.Ordinal))
        {
            identity.AddClaim(new Claim(RoleClaim, role));
        }

        return Task.CompletedTask;
    }

    private static IEnumerable<string> RolesIn(string? json, params string[] path)
    {
        if (string.IsNullOrEmpty(json))
        {
            return [];
        }

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
}
