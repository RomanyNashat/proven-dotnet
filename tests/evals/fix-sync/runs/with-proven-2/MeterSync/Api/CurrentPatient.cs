using System.Globalization;
using System.Security.Claims;

namespace MeterSync.Api;

public static class CurrentPatient
{
    /// <summary>The caller's patient id: the token's <c>sub</c>.</summary>
    /// <param name="user">The caller.</param>
    /// <param name="patientId">The id, when <c>sub</c> is one.</param>
    /// <returns>False when <c>sub</c> is missing or isn't a patient id; the endpoint answers 401, not 500.</returns>
    public static bool TryGetId(ClaimsPrincipal user, out int patientId)
    {
        // MapInboundClaims is off, so "sub" stays "sub"; NameIdentifier covers a handler that still maps it.
        var sub = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(sub, NumberStyles.None, CultureInfo.InvariantCulture, out patientId) && patientId > 0;
    }
}
