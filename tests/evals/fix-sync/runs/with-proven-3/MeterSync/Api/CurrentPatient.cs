using System.Globalization;
using System.Security.Claims;

namespace MeterSync.Api;

public static class CurrentPatient
{
    /// <summary>The raw subject: "sub", or NameIdentifier when the JWT handler has mapped it.</summary>
    public static string? Subject(ClaimsPrincipal user) =>
        user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>
    /// The patient id the token's subject carries. False when there is no subject or it isn't a patient id:
    /// that token isn't an identity this API accepts (401), not a server error.
    /// </summary>
    public static bool TryGetId(ClaimsPrincipal user, out int id) =>
        int.TryParse(Subject(user), NumberStyles.None, CultureInfo.InvariantCulture, out id);
}
