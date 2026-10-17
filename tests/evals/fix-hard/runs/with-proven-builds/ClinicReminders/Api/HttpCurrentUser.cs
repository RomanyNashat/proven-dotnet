using System.Globalization;
using ClinicReminders.Application;

namespace ClinicReminders.Api;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    // JwtBearer runs with MapInboundClaims = false, so "sub" arrives as "sub".
    public bool TryGetPatientId(out int patientId)
    {
        patientId = 0;
        var sub = accessor.HttpContext?.User.FindFirst("sub")?.Value;
        return int.TryParse(sub, NumberStyles.None, CultureInfo.InvariantCulture, out patientId) && patientId > 0;
    }
}
