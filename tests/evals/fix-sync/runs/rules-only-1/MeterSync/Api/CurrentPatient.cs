using System.Globalization;
using System.Security.Claims;

namespace MeterSync.Api;

public static class CurrentPatient
{
    /// <summary>Reads the patient's id from the token's <c>sub</c>, whether or not inbound claims were mapped.</summary>
    /// <param name="user">The authenticated caller.</param>
    /// <param name="id">The patient id when the claim holds one.</param>
    /// <returns><c>false</c> when the token carries no usable patient id.</returns>
    public static bool TryGetId(ClaimsPrincipal user, out int id)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
    }
}
