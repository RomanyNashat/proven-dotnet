using ClinicReminders.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicReminders.Infrastructure;

public sealed class ReminderConfiguration : IEntityTypeConfiguration<Reminder>
{
    /// <summary>Matches the column, so a longer value is a 400 before it reaches SQL Server.</summary>
    public const int MobileMaxLength = 20;

    public void Configure(EntityTypeBuilder<Reminder> builder)
    {
        builder.Property(r => r.Id).UseIdentityColumn();
        builder.Property(r => r.Mobile).HasMaxLength(MobileMaxLength).IsUnicode(false);
        builder.Property(r => r.AppointmentAt).IsUtcDateTime2();
        builder.Property(r => r.CreatedAt).IsUtcDateTime2();
        builder.Property(r => r.SentAt).IsUtcDateTime2();
        builder.Property(r => r.ClaimedAt).IsUtcDateTime2();

        // The reminders screen: one patient's rows, keyset-paged by (CreatedAt, Id).
        builder.HasIndex(r => new { r.PatientId, r.CreatedAt, r.Id });

        // The job's due query. Literal IS NULL filters, which the generated SQL repeats, so the index is used.
        builder.HasIndex(r => r.AppointmentAt)
            .HasDatabaseName("IX_Reminders_Due")
            .HasFilter("[SentAt] IS NULL AND [ClaimedAt] IS NULL");
    }
}
