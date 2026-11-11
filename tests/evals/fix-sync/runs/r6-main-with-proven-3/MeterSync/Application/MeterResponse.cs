namespace MeterSync.Application;

/// <summary>A meter linked to the patient, as <c>GET /meters</c> returns it.</summary>
public sealed record MeterResponse(string SerialNumber, DateTimeOffset LinkedAt);
