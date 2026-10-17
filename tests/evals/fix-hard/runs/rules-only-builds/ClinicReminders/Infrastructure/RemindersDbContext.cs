using ClinicReminders.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClinicReminders.Infrastructure;

public sealed class RemindersDbContext(DbContextOptions<RemindersDbContext> options) : DbContext(options)
{
    public DbSet<Reminder> Reminders => Set<Reminder>();
    public DbSet<AuditEntry> Audit => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Reminder>(e =>
        {
            e.Property(r => r.Mobile).HasMaxLength(20).IsUnicode(false);
            e.Property(r => r.AppointmentAt).HasColumnType("datetimeoffset(3)");
            e.Property(r => r.CreatedAt).HasColumnType("datetimeoffset(3)");
            e.Property(r => r.Status).HasColumnType("tinyint").HasConversion<byte>();
            e.Property(r => r.ClaimedAt).HasColumnType("datetimeoffset(3)");
            e.Property(r => r.SentAt).HasColumnType("datetimeoffset(3)");

            // The reminders screen: one patient's reminders, oldest first, keyset-paged on (CreatedAt, Id).
            e.HasIndex(r => new { r.PatientId, r.CreatedAt, r.Id });

            // The send job: pending reminders whose appointment is within the next day.
            e.HasIndex(r => new { r.Status, r.AppointmentAt });
        });
        modelBuilder.Entity<AuditEntry>(e =>
        {
            e.Property(a => a.Action).HasMaxLength(100).IsUnicode(false);
            e.Property(a => a.At).HasColumnType("datetimeoffset(3)");
        });
    }
}
