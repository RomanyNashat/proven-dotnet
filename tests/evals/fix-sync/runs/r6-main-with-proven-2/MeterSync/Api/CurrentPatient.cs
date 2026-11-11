using System.Globalization;
using System.Security.Claims;

namespace MeterSync.Api;

public static class CurrentPatient
{
    /// <summary>
    /// The caller's patient id from <c>sub</c> (mapped to NameIdentifier by the JWT handler). False when it
    /// is missing or not a positive int, so the endpoint answers 401 instead of throwing.
    /// </summary>
    public static bool TryGetId(ClaimsPrincipal user, out int id)
    {
        var subject = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(subject, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
    }
}
