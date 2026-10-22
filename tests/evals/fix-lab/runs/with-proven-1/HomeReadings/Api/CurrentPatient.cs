using System.Globalization;
using System.Security.Claims;

namespace HomeReadings.Api;

public interface ICurrentPatient
{
    int Id { get; }
}

/// <summary>The patient is the token's <c>sub</c>, an int. The authorization policy refuses any other token.</summary>
public sealed class HttpCurrentPatient(IHttpContextAccessor accessor) : ICurrentPatient
{
    public const string SubjectClaim = "sub";

    public int Id => TryRead(accessor.HttpContext?.User, out var id)
        ? id
        : throw new InvalidOperationException("The request has no patient; the authorization policy should have refused it.");

    /// <summary>Reads the patient id from <c>sub</c> (or its mapped name, if inbound mapping is ever on).</summary>
    public static bool TryRead(ClaimsPrincipal? user, out int id) =>
        int.TryParse(user?.FindFirstValue(SubjectClaim) ?? user?.FindFirstValue(ClaimTypes.NameIdentifier),
            NumberStyles.None, CultureInfo.InvariantCulture, out id);
}
