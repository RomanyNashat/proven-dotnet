using System.Globalization;
using System.Security.Claims;

namespace MeterSync.Api;

public static class CurrentPatient
{
    /// <summary>The caller's patient id from <c>sub</c> (mapped to NameIdentifier by default).</summary>
    /// <param name="user">The authenticated caller.</param>
    /// <returns>The id, or <c>null</c> when the token carries none that is a number.</returns>
    public static int? Id(ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return int.TryParse(sub, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;
    }
}
