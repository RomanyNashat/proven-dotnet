namespace LabResults.Api;

public sealed record ResultResponse(int Id, int PatientId, string Test, decimal Value, string Unit, string? Notes, string RecordedBy, DateTimeOffset ReceivedAt);
