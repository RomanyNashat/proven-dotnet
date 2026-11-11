using System.Globalization;
using System.Security.Claims;

namespace MeterSync.Api;

public static class CurrentPatient
{
    /// <summary>The patient id from the token's <c>sub</c> claim.</summary>
    /// <returns>The id, or <c>null</c> when the claim is missing or isn't a positive integer.</returns>
    public static int? TryGetId(ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue("sub"), NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
            ? id
            : null;
}
