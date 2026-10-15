namespace SkillSamples.PiiMasking;

/// <summary>Logged whole with [LogProperties]: every property becomes a field, today's and any added later.</summary>
public sealed record BookingRequest(int BookingId, [PatientIdentifier] string NationalId, string Clinic, string? Mobile);
