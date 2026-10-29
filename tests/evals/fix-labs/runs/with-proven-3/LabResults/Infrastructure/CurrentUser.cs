using System.Security.Claims;

namespace LabResults.Infrastructure;

/// <summary>The signed-in user for this request. Read on each access, never cached at construction.</summary>
public sealed class CurrentUser(IHttpContextAccessor accessor)
{
    /// <summary>The caller's Keycloak <c>sub</c>, or <c>null</c> when there is no signed-in caller.</summary>
    public string? Id => IdOf(accessor.HttpContext?.User);

    /// <summary>The <c>sub</c> of a principal.</summary>
    /// <param name="user">The principal, if any.</param>
    /// <returns>The <c>sub</c>, or <c>null</c>.</returns>
    public static string? IdOf(ClaimsPrincipal? user) =>
        user?.FindFirstValue("sub") ?? user?.FindFirstValue(ClaimTypes.NameIdentifier);
}
