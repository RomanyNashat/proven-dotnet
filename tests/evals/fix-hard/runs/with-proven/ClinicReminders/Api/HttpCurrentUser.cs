using System.Globalization;
using System.Security.Claims;
using ClinicReminders.Application;

namespace ClinicReminders.Api;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    // JwtBearer keeps "sub" as it is (MapInboundClaims = false). NameIdentifier covers schemes that put the
    // subject there instead, such as a test scheme. A missing or non-numeric subject is null, not a 500.
    public int? Id
    {
        get
        {
            var user = accessor.HttpContext?.User;
            var subject = user?.FindFirst("sub")?.Value ?? user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(subject, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;
        }
    }
}
