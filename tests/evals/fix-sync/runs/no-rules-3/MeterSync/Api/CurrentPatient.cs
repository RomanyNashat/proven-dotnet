using System.Globalization;
using System.Security.Claims;

namespace MeterSync.Api;

public static class CurrentPatient
{
    /// <summary>The patient id in the token's <c>sub</c> claim, or null if the token doesn't carry a valid one.</summary>
    public static int? Id(ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub"),
            NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;
}
