using System.Globalization;
using System.Security.Claims;

namespace MeterSync.Api;

public static class CurrentPatient
{
    /// <summary>The caller's patient id, from the token's <c>sub</c>.</summary>
    /// <param name="user">The caller.</param>
    /// <param name="id">The patient id.</param>
    /// <returns>False when the token carries no numeric subject.</returns>
    public static bool TryGetId(ClaimsPrincipal user, out int id)
    {
        // JwtBearer maps "sub" to NameIdentifier unless MapInboundClaims is off; accept both.
        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return int.TryParse(sub, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
    }
}
