using System.Security.Claims;

namespace LabResults.Infrastructure;

/// <summary>The signed-in user for this request.</summary>
public sealed class CurrentUser(IHttpContextAccessor accessor)
{
    /// <summary>The token's <c>sub</c>. Throws rather than inventing an id, so an unattributed write never saves.</summary>
    /// <exception cref="InvalidOperationException">There is no request or the user has no subject.</exception>
    public string Id =>
        accessor.HttpContext?.User.FindFirstValue("sub")
        ?? accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("No signed-in user with a subject claim.");
}
