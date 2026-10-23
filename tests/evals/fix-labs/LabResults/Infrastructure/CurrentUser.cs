using System.Security.Claims;

namespace LabResults.Infrastructure;

/// <summary>The signed-in user for this request.</summary>
public sealed class CurrentUser(IHttpContextAccessor accessor)
{
    public string Id { get; } = accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
                                ?? accessor.HttpContext?.User.FindFirstValue("sub")
                                ?? "system";
}
