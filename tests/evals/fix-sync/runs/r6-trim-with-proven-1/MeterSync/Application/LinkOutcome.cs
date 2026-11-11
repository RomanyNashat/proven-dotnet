namespace MeterSync.Application;

public enum LinkOutcome
{
    Linked,

    /// <summary>The caller has no patient record, or the national id isn't theirs. One answer for both, so the
    /// endpoint can't be used to learn which national ids exist.</summary>
    PatientNotFound,

    AlreadyLinked,
}
