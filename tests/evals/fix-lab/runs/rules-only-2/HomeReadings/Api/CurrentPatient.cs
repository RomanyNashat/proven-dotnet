using System.Globalization;
using System.Security.Claims;
using HomeReadings.Application;

namespace HomeReadings.Api;

public sealed class HttpCurrentPatient(IHttpContextAccessor accessor) : ICurrentPatient
{
    public int Id => TryGetPatientId(accessor.HttpContext?.User, out var id)
        ? id
        : throw new InvalidOperationException("The request has no patient. Endpoints that use ICurrentPatient require the patient policy.");

    /// <summary>The patient's id is the token's <c>sub</c>, a positive integer.</summary>
    public static bool TryGetPatientId(ClaimsPrincipal? user, out int id)
    {
        id = 0;
        var sub = user?.FindFirstValue("sub") ?? user?.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(sub, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
    }
}
