using System.Globalization;
using System.Security.Claims;

namespace MeterSync.Api;

public static class CurrentPatient
{
    /// <summary>Reads the patient id from the token's subject (mapped to NameIdentifier by default, or raw <c>sub</c>).</summary>
    /// <param name="user">The authenticated caller.</param>
    /// <param name="patientId">The patient id, when the subject is a positive integer.</param>
    /// <returns>False when the subject is missing or isn't a patient id.</returns>
    public static bool TryGetId(ClaimsPrincipal user, out int patientId)
    {
        var subject = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return int.TryParse(subject, NumberStyles.None, CultureInfo.InvariantCulture, out patientId) && patientId > 0;
    }
}
