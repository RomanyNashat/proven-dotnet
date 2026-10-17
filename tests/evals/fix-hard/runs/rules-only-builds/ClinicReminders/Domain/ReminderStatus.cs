namespace ClinicReminders.Domain;

/// <summary>
/// Where a reminder is in its one delivery. The SMS provider has no idempotency key, so sending twice means two
/// messages: a reminder leaves <see cref="Pending"/> before the provider is called, and only goes back when the
/// call certainly never reached the provider.
/// </summary>
public enum ReminderStatus
{
    /// <summary>Waiting to be sent.</summary>
    Pending = 0,

    /// <summary>Claimed by a job run and being sent. One that stays here means the process stopped mid-send.</summary>
    Sending = 1,

    Sent = 2,

    /// <summary>The provider refused it (bad number, bad request). Sending it again would fail the same way.</summary>
    Rejected = 3,

    /// <summary>The provider may or may not have sent it (timeout, 5xx, dropped connection). Not retried, to avoid a duplicate SMS.</summary>
    Unknown = 4,
}
