using System.Security.Claims;

namespace LabResults.Infrastructure;

/// <summary>The signed-in user for this request.</summary>
public sealed class CurrentUser(IHttpContextAccessor accessor)
{
    /// <summary>
    /// The caller's Keycloak <c>sub</c>, or null when there is no signed-in user. No "system" fallback: a
    /// save that needs a caller and has none must fail, not be stamped with a made-up name.
    /// </summary>
    public string? Id => accessor.HttpContext?.User is { } user
        ? user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
        : null;
}
