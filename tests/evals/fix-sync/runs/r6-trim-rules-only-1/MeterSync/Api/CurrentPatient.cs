using System.Globalization;
using System.Security.Claims;

namespace MeterSync.Api;

public static class CurrentPatient
{
    /// <summary>The caller's patient id, from the token's <c>sub</c>.</summary>
    /// <param name="user">The authenticated caller.</param>
    /// <param name="id">The patient id, when the token carries one.</param>
    /// <returns>False when <c>sub</c> is missing or isn't a patient id.</returns>
    public static bool TryGetId(ClaimsPrincipal user, out int id) =>
        int.TryParse(
            user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out id) && id > 0;
}
