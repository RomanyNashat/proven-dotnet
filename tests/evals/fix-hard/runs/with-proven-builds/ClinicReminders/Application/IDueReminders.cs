namespace ClinicReminders.Application;

/// <summary>What the job needs to send one reminder: no tracked entity, so nothing waits on a SaveChanges.</summary>
public sealed record DueReminder(int Id, string Mobile, DateTimeOffset AppointmentAt);

/// <summary>Appointments strictly after <paramref name="From"/> and before <paramref name="Until"/>.</summary>
public sealed record DueWindow(DateTimeOffset From, DateTimeOffset Until);

public interface IDueReminders
{
    /// <summary>Unsent, unclaimed reminders in the window, by id, after <paramref name="afterId"/>.</summary>
    Task<IReadOnlyList<DueReminder>> FindAsync(DueWindow window, int afterId, int size, CancellationToken ct);

    /// <summary>True only for the one caller that claimed the row; false when it's sent or claimed already.</summary>
    Task<bool> TryClaimAsync(int id, DateTimeOffset now, CancellationToken ct);

    Task MarkSentAsync(int id, DateTimeOffset at, CancellationToken ct);

    /// <summary>Gives a claimed row back for the next run: only when the message certainly didn't go out.</summary>
    Task ReleaseAsync(int id, CancellationToken ct);
}
