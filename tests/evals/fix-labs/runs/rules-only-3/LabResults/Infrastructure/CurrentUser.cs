using System.Security.Claims;

namespace LabResults.Infrastructure;

/// <summary>The signed-in user for this request.</summary>
public sealed class CurrentUser(IHttpContextAccessor accessor)
{
    /// <summary>
    /// The token's <c>sub</c>. Every caller here is authenticated, so a missing <c>sub</c> is a bug, not a
    /// reason to record or list results under a shared fallback identity.
    /// </summary>
    public string Id => (accessor.HttpContext?.User is { } user
                         ? user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
                         : null) is { Length: > 0 } sub
        ? sub
        : throw new InvalidOperationException("The request has no authenticated subject.");
}
