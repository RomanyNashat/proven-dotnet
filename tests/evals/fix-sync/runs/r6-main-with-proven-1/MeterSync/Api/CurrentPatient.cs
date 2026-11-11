using System.Globalization;
using System.Security.Claims;

namespace MeterSync.Api;

/// <summary>The patient the bearer token belongs to.</summary>
public static class CurrentPatient
{
    /// <summary>Reads the patient id from the token's subject.</summary>
    /// <param name="user">The authenticated caller.</param>
    /// <param name="id">The patient id, when the subject is a positive integer.</param>
    /// <returns><see langword="false"/> when the subject is missing or isn't a patient id.</returns>
    public static bool TryGetId(ClaimsPrincipal user, out int id)
    {
        var subject = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return int.TryParse(subject, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
    }
}
