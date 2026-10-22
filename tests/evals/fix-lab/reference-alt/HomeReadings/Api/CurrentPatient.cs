using System.Security.Claims;

namespace HomeReadings.Api;

public interface ICurrentPatient
{
    int Id { get; }
}

public sealed class HttpCurrentPatient(IHttpContextAccessor accessor) : ICurrentPatient
{
    public int Id => int.Parse(accessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!, System.Globalization.CultureInfo.InvariantCulture);
}
