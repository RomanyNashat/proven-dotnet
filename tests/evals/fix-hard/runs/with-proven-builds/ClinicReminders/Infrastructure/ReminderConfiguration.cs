using ClinicReminders.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicReminders.Infrastructure;

public sealed class ReminderConfiguration : IEntityTypeConfiguration<Reminder>
{
    public void Configure(EntityTypeBuilder<Reminder> builder)
    {
        builder.ToTable("Reminders");
        builder.Property(r => r.Id).UseIdentityColumn();
        builder.Property(r => r.Mobile).HasMaxLength(20).IsUnicode(false);   // ASCII digits and '+', validated
        builder.Property(r => r.AppointmentAt).AsUtc();
        builder.Property(r => r.CreatedAt).AsUtc();
        builder.Property(r => r.SentAt).AsUtc();
        builder.Property(r => r.ClaimedAt).AsUtc();

        // The reminders screen: one patient's rows in (CreatedAt, Id) order, the cursor's key.
        builder.HasIndex(r => new { r.PatientId, r.CreatedAt, r.Id });

        // The job: rows nobody has claimed yet, by appointment time.
        builder.HasIndex(r => r.AppointmentAt)
            .HasDatabaseName("IX_Reminders_Due")
            .HasFilter("[SentAt] IS NULL AND [ClaimedAt] IS NULL");
    }
}
