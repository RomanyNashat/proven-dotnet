using System.Globalization;
using System.Security.Claims;

namespace MeterSync.Api;

public static class CurrentPatient
{
    /// <summary>The patient id from the token's <c>sub</c> (mapped to NameIdentifier by default).</summary>
    /// <returns>False when the token carries no numeric subject.</returns>
    public static bool TryGetId(ClaimsPrincipal user, out int id) =>
        int.TryParse(
            user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub"),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out id)
        && id > 0;
}
