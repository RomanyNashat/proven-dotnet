using System.Globalization;
using System.Security.Claims;

namespace HomeReadings.Api;

public interface ICurrentPatient
{
    int Id { get; }
}

public sealed class HttpCurrentPatient(IHttpContextAccessor accessor) : ICurrentPatient
{
    public int Id
    {
        get
        {
            var user = accessor.HttpContext?.User;
            var sub = user?.FindFirstValue("sub") ?? user?.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(sub, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                ? id
                : throw new UnauthorizedAccessException("Token has no valid patient id.");
        }
    }
}
