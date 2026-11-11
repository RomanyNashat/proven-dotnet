using System.Globalization;
using System.Security.Claims;

namespace MeterSync.Api;

public static class CurrentPatient
{
    /// <summary>The caller's patient id, from the token's <c>sub</c>.</summary>
    /// <param name="user">The authenticated caller.</param>
    /// <returns>The id, or <c>null</c> when the token has no numeric subject.</returns>
    public static int? Id(ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub"),
            NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;
}
