using System.Globalization;
using System.Security.Claims;

namespace MeterSync.Api;

public static class CurrentPatient
{
    /// <summary>Reads the patient id from the token's subject.</summary>
    /// <param name="user">The authenticated caller.</param>
    /// <param name="id">The patient id, when the subject is one.</param>
    /// <returns>False when the token carries no integer subject.</returns>
    public static bool TryGetId(ClaimsPrincipal user, out int id) =>
        int.TryParse(
            user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub"),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out id);
}
