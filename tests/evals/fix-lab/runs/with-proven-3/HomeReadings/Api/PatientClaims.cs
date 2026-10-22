using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace HomeReadings.Api;

/// <summary>The patient is the token's <c>sub</c>, which must be a positive integer.</summary>
public static class PatientClaims
{
    public const string Policy = "patient";

    /// <summary>Reads the patient id from <c>sub</c> (or NameIdentifier, if inbound claims were mapped).</summary>
    public static bool TryGetPatientId(ClaimsPrincipal user, out int patientId)
    {
        var sub = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(sub, NumberStyles.None, CultureInfo.InvariantCulture, out patientId) && patientId > 0;
    }

    /// <summary>A signed-in caller whose <c>sub</c> isn't a patient id gets 403, not a 500 from a parse.</summary>
    public static AuthorizationBuilder AddPatientPolicy(this AuthorizationBuilder builder) =>
        builder.AddPolicy(Policy, p => p.RequireAuthenticatedUser().RequireAssertion(c => TryGetPatientId(c.User, out _)));
}
