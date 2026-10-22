using HomeReadings.Application;

namespace HomeReadings.Api;

/// <summary>The patient on the current request. Endpoints that use it require <see cref="PatientClaims.Policy"/>.</summary>
public sealed class HttpCurrentPatient(IHttpContextAccessor accessor) : ICurrentPatient
{
    public int Id => accessor.HttpContext is { } http && PatientClaims.TryGetPatientId(http.User, out var id)
        ? id
        : throw new InvalidOperationException("No patient on this request; the endpoint must require the patient policy.");
}
