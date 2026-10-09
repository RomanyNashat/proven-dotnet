using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Validation.AspNetCore;

namespace SkillSamples.Auth;

/// <summary>
/// Tokens from your own OpenIddict server. It can issue either kind; the server's
/// configuration decides, and the resource service registers the matching one.
/// </summary>
public static class PortalTokens
{
    /// <summary>
    /// JWT access tokens, validated by each service like Keycloak's. The server must turn access-token
    /// encryption off (DisableAccessTokenEncryption): OpenIddict encrypts them by default, and JwtBearer
    /// rejects every encrypted token. OpenIddict's role claim is "role", a plain claim: no mapping needed.
    /// </summary>
    public static AuthenticationBuilder AddPortalJwt(this IServiceCollection services, IConfiguration config) =>
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = config["Auth:Authority"];
                options.Audience = config["Auth:Audience"];
                options.RequireHttpsMetadata = true;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "sub",
                    RoleClaimType = "role",
                };
            });

    /// <summary>
    /// Reference (opaque) access tokens: the service can't read them, so every request asks the server's
    /// introspection endpoint, authenticating as its own client. A revoked token fails on the next request.
    /// </summary>
    public static IServiceCollection AddPortalIntrospection(this IServiceCollection services, IConfiguration config)
    {
        services.AddOpenIddict().AddValidation(options =>
        {
            options.SetIssuer(new Uri(config["Auth:Authority"]!));
            options.UseIntrospection()
                .SetClientId(config["Auth:ClientId"]!)
                .SetClientSecret(config["Auth:ClientSecret"]!);
            options.UseSystemNetHttp();
            options.UseAspNetCore();
        });
        services.AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        return services;
    }
}
