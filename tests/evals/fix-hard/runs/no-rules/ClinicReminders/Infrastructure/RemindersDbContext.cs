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
            e.Property(r => r.SentAt).HasColumnType("datetimeoffset(3)");
            e.HasIndex(r => new { r.PatientId, r.CreatedAt, r.Id });
            e.HasIndex(r => new { r.SentAt, r.AppointmentAt });
        });
        modelBuilder.Entity<AuditEntry>(e =>
        {
            e.Property(a => a.Action).HasMaxLength(100);
            e.Property(a => a.At).HasColumnType("datetimeoffset(3)");
        });
    }
}
