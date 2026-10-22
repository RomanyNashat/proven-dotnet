using System.Globalization;
using System.Security.Claims;

namespace HomeReadings.Api;

public interface ICurrentPatient
{
    int Id { get; }
}

public static class PatientClaims
{
    /// <summary>The token's <c>sub</c> is the patient's id.</summary>
    public static bool TryGetPatientId(ClaimsPrincipal user, out int id)
    {
        var sub = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(sub, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
    }
}

public sealed class HttpCurrentPatient(IHttpContextAccessor accessor) : ICurrentPatient
{
    // The authorization policy only lets a request through when sub is a patient id, so this holds
    // inside an endpoint.
    public int Id => accessor.HttpContext?.User is { } user && PatientClaims.TryGetPatientId(user, out var id)
        ? id
        : throw new InvalidOperationException("No patient on this request.");
}
