using System.Globalization;
using System.Security.Claims;

namespace MeterSync.Api;

public static class CurrentPatient
{
    public static int? Id(ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub"),
            NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;
}
