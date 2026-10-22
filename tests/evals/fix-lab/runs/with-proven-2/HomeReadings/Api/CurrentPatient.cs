namespace HomeReadings.Api;

public interface ICurrentPatient
{
    int Id { get; }
}

public sealed class HttpCurrentPatient(IHttpContextAccessor accessor) : ICurrentPatient
{
    // The authorization policy only lets requests through whose subject is a patient id, so this can't
    // fail on an endpoint; it throws rather than guess if it is ever used outside one.
    public int Id => accessor.HttpContext?.User is { } user && PatientClaims.TryGetPatientId(user, out var id)
        ? id
        : throw new InvalidOperationException("The request has no authenticated patient.");
}
