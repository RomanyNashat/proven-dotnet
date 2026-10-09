using Microsoft.Extensions.DependencyInjection;

namespace SkillSamples.Owasp;

public static class PortalCors
{
    public const string Policy = "portal";

    // Only the portal's own origins, from configuration. No AllowCredentials: the portal sends the token in
    // the Authorization header, and credentials are only for cookies. Never SetIsOriginAllowed(_ => true)
    // with AllowCredentials: it echoes back any origin and lets any site call the API as the signed-in user.
    public static IServiceCollection AddPortalCors(this IServiceCollection services, string[] origins) =>
        services.AddCors(o => o.AddPolicy(Policy, policy => policy
            .WithOrigins(origins)
            .WithMethods("GET", "POST", "PUT", "DELETE")
            .WithHeaders("Authorization", "Content-Type")));
}
