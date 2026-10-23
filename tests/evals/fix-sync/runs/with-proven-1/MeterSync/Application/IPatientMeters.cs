namespace MeterSync.Application;

public interface IPatientMeters
{
    Task<IReadOnlyList<MeterSummary>> MetersOfAsync(int patientId, CancellationToken ct);
}
