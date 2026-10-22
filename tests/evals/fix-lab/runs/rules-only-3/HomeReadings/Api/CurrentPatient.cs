using System.Globalization;
using System.Security.Claims;

namespace HomeReadings.Api;

public interface ICurrentPatient
{
    int Id { get; }
}

/// <summary>The patient is the token's <c>sub</c>. The authorization policy only lets a request in when it is a patient id.</summary>
public sealed class HttpCurrentPatient(IHttpContextAccessor accessor) : ICurrentPatient
{
    public int Id => TryGetId(accessor.HttpContext?.User, out var id)
        ? id
        : throw new InvalidOperationException("The request has no patient id; the endpoint must require the patient policy.");

    public static bool TryGetId(ClaimsPrincipal? user, out int id)
    {
        id = 0;

        // The JWT handler maps "sub" to NameIdentifier unless inbound claim mapping is turned off.
        var sub = user?.FindFirstValue("sub") ?? user?.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(sub, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
    }
}
