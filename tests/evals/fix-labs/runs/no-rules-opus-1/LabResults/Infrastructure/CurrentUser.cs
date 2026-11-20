using System.Security.Claims;

namespace LabResults.Infrastructure;

/// <summary>The signed-in user for the current request, read at the moment it is asked for.</summary>
/// <remarks>
/// Singleton-safe: it holds no per-request state itself, it reads the ambient <see cref="HttpContext"/> each time.
/// </remarks>
public sealed class CurrentUser(IHttpContextAccessor accessor)
{
    /// <summary>The Keycloak <c>sub</c> of the authenticated user, or <c>null</c> when there is none.</summary>
    public string? Id
    {
        get
        {
            var user = accessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            return user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        }
    }
}
