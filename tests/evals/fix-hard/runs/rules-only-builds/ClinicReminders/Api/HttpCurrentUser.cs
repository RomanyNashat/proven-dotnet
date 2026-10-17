using System.Globalization;
using System.Security.Claims;
using ClinicReminders.Application;

namespace ClinicReminders.Api;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public const string StaffRole = "clinic-staff";

    private ClaimsPrincipal User =>
        accessor.HttpContext?.User ?? throw new InvalidOperationException("No HTTP request: there is no current user.");

    public int Id =>
        SubjectOf(User) ?? throw new InvalidOperationException("The caller has no numeric subject; the authorization policy should have refused the request.");

    public bool IsStaff => User.IsInRole(StaffRole) || User.HasClaim("role", StaffRole);

    /// <summary>
    /// The caller's numeric subject, or null. Reads "sub", and also the mapped NameIdentifier claim in case a
    /// handler maps inbound claims.
    /// </summary>
    public static int? SubjectOf(ClaimsPrincipal user)
    {
        var value = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;
    }
}
