using System.Globalization;
using System.Security.Claims;

namespace HomeReadings.Api;

/// <summary>The token's <c>sub</c> is the patient's id.</summary>
public static class PatientClaims
{
    public const string Subject = "sub";

    /// <summary>Reads the patient id from <c>sub</c> (or its mapped name, if claim mapping is on).</summary>
    /// <param name="user">The authenticated user.</param>
    /// <param name="patientId">The patient id, when the subject is one.</param>
    /// <returns><c>false</c> when there is no subject or it isn't an integer.</returns>
    public static bool TryGetPatientId(ClaimsPrincipal user, out int patientId) =>
        int.TryParse(user.FindFirstValue(Subject) ?? user.FindFirstValue(ClaimTypes.NameIdentifier),
            NumberStyles.None, CultureInfo.InvariantCulture, out patientId);
}
