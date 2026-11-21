using System.Security.Claims;

namespace LabResults.Infrastructure;

/// <summary>The signed-in user for the current request.</summary>
public sealed class CurrentUser(IHttpContextAccessor accessor)
{
    /// <summary>The token's <c>sub</c>, or null when there is no authenticated user.</summary>
    public string? Id => SubjectOf(accessor.HttpContext?.User);

    public static string? SubjectOf(ClaimsPrincipal? user) =>
        user?.Identity?.IsAuthenticated == true
            ? user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
            : null;
}
