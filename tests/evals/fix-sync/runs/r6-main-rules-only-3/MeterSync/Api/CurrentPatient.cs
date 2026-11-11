using System.Globalization;
using System.Security.Claims;

namespace MeterSync.Api;

public static class CurrentPatient
{
    /// <summary>The caller's patient id from the token's <c>sub</c>, or <c>null</c> when it isn't one.</summary>
    /// <param name="user">The caller.</param>
    /// <returns>The patient id, or <c>null</c>.</returns>
    public static int? Id(ClaimsPrincipal user)
    {
        // JwtBearer maps "sub" to NameIdentifier unless MapInboundClaims is off; accept either.
        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return int.TryParse(sub, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;
    }
}
