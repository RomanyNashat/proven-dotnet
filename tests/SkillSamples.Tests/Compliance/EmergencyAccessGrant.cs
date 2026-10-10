namespace SkillSamples.Compliance;

// Break-the-glass: a clinician without the usual permission opens one patient's record in an emergency.
// It needs a reason someone can review, covers one patient, and ends on its own.
public sealed record EmergencyAccessGrant(string UserId, int PatientId, string Reason, DateTimeOffset ExpiresAt)
{
    public static readonly TimeSpan Lasts = TimeSpan.FromMinutes(30);

    public static EmergencyAccessGrant Open(string userId, int patientId, string reason, TimeProvider time) =>
        string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 10
            ? throw new ArgumentException("Emergency access needs a reason someone can review later.", nameof(reason))
            : new(userId, patientId, reason.Trim(), time.GetUtcNow() + Lasts);

    public bool Allows(int patientId, TimeProvider time) => patientId == PatientId && time.GetUtcNow() < ExpiresAt;
}
