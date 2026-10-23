namespace MeterSync.Application;

/// <summary>A meter linked to the patient, as the patient's own list shows it.</summary>
public sealed record LinkedMeter(string SerialNumber, DateTimeOffset LinkedAt);
