using System.Globalization;
using System.Security.Claims;

namespace MeterSync.Api;

public static class CurrentPatient
{
    /// <summary>The patient's id from the token's <c>sub</c>. False when it's missing or not a patient id.</summary>
    public static bool TryGetId(ClaimsPrincipal user, out int id) =>
        int.TryParse(user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier),
            NumberStyles.None, CultureInfo.InvariantCulture, out id);
}
