namespace ClinicReminders.Domain;

/// <summary>Where a reminder is in sending. The SMS provider has no idempotency key, so a reminder is sent at most once.</summary>
public enum ReminderStatus
{
    /// <summary>Waiting for the job.</summary>
    Pending = 0,

    /// <summary>Claimed by a runner, which is calling the provider. A row left here (crash, shutdown) is not retried.</summary>
    Sending = 1,

    /// <summary>The provider accepted it.</summary>
    Sent = 2,

    /// <summary>The provider refused it (4xx); retrying won't help.</summary>
    Failed = 3,

    /// <summary>It may or may not have been delivered (timeout, 5xx, lost response); not retried, to avoid a duplicate.</summary>
    Unknown = 4,
}
