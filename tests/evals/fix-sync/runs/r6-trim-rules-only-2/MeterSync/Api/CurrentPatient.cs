using System.Globalization;
using System.Security.Claims;

namespace MeterSync.Api;

public static class CurrentPatient
{
    /// <summary>The patient id from the token's <c>sub</c>.</summary>
    /// <param name="user">The authenticated user.</param>
    /// <returns>The id, or null when the token carries no usable patient id.</returns>
    public static int? Id(ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub"),
            NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
            ? id
            : null;
}
