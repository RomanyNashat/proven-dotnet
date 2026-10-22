using System.Globalization;

namespace HomeReadings.Api;

public interface ICurrentPatient
{
    int Id { get; }
}

public sealed class HttpCurrentPatient(IHttpContextAccessor accessor) : ICurrentPatient
{
    // The JWT "sub" claim (inbound claim mapping is off, so it keeps its name).
    public int Id => int.TryParse(accessor.HttpContext?.User.FindFirst("sub")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
        ? id
        : throw new UnauthorizedAccessException("Token has no valid patient id");
}
