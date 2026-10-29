using System.Security.Claims;

namespace LabResults.Infrastructure;

/// <summary>The signed-in user for this request.</summary>
public sealed class CurrentUser(IHttpContextAccessor accessor)
{
    public string? Id => accessor.HttpContext?.User.FindFirstValue("sub")
                         ?? accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
}
