using System.Globalization;
using System.Security.Claims;

namespace MeterSync.Api;

public static class CurrentPatient
{
    /// <summary>The patient id from the token's <c>sub</c>, or null when it is missing or not a patient id.</summary>
    /// <param name="user">The authenticated caller.</param>
    /// <returns>The patient id, or null.</returns>
    public static int? Id(ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return int.TryParse(sub, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;
    }
}
