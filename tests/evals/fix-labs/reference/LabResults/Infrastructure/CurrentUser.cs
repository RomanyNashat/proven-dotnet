using System.Security.Claims;

namespace LabResults.Infrastructure;

/// <summary>The signed-in user for this request.</summary>
public sealed class CurrentUser(IHttpContextAccessor accessor)
{
    // Read on every call: the pooled context's interceptor holds one instance for the life of the app.
    public string Id => accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? accessor.HttpContext?.User.FindFirstValue("sub")
                        ?? "system";
}
