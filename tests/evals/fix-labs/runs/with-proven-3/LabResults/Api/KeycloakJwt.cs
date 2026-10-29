using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace LabResults.Api;

/// <summary>JWT bearer for Keycloak tokens (auth-patterns §1).</summary>
public static class KeycloakJwt
{
    public const string Audience = "lab-results";
    public const string RoleClaim = "roles";

    /// <summary>JWT bearer on the default scheme, authority <c>Auth:Authority</c>, audience <c>lab-results</c>.</summary>
    /// <param name="services">The services.</param>
    /// <param name="config">Read when the options are built, so test overrides apply.</param>
    /// <returns>The authentication builder.</returns>
    public static AuthenticationBuilder AddKeycloakJwt(this IServiceCollection services, IConfiguration config) =>
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.Authority = config["Auth:Authority"];
                o.Audience = Audience;
                o.RequireHttpsMetadata = true;
                o.MapInboundClaims = false;   // keep "sub" as "sub": it is what recordedBy stores
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidAudience = Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "preferred_username",
                    RoleClaimType = RoleClaim,
                };
                o.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        if (context.Principal?.Identity is ClaimsIdentity identity)
                        {
                            AddRoles(identity, Audience);
                        }

                        return Task.CompletedTask;
                    },
                };
            });

    /// <summary>
    /// Keycloak puts roles inside JSON claims, <c>realm_access.roles</c> and
    /// <c>resource_access.&lt;client&gt;.roles</c>. <c>RequireRole</c> sees none of them until each is copied
    /// into its own role claim.
    /// </summary>
    /// <param name="identity">The validated identity; claims are added with its own role claim type.</param>
    /// <param name="client">The Keycloak client whose roles count.</param>
    public static void AddRoles(ClaimsIdentity identity, string client)
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
        if (string.IsNullOrEmpty(json))
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
            return [];   // a signed token with a malformed claim grants no role; the 403 follows
        }
    }
}
