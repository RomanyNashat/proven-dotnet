using LabResults.Domain;
using Microsoft.EntityFrameworkCore;

namespace LabResults.Infrastructure;

public sealed class LabDbContext(DbContextOptions<LabDbContext> options) : DbContext(options)
{
    public DbSet<LabResult> Results => Set<LabResult>();

    public DbSet<SmsMessage> SmsOutbox => Set<SmsMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LabResult>(e =>
        {
            e.ToTable("lab_results");
            e.Property(r => r.Test).HasMaxLength(LabResult.TestMaxLength);
            e.Property(r => r.Unit).HasMaxLength(LabResult.UnitMaxLength);
            e.Property(r => r.Notes).HasMaxLength(LabResult.NotesMaxLength);
            e.Property(r => r.Value).HasColumnType("numeric(10,3)");
            e.Property(r => r.ReceivedAt).HasColumnType("timestamptz");
            e.Property(r => r.RecordedBy).HasMaxLength(64);

            // Keyset paging orders by (ReceivedAt, Id) descending.
            e.HasIndex(r => new { r.ReceivedAt, r.Id }).IsDescending(true, true);
            e.HasIndex(r => r.PatientId);
        });

        modelBuilder.Entity<SmsMessage>(e =>
        {
            e.ToTable("sms_outbox");
            e.Property(m => m.To).HasMaxLength(16);
            e.Property(m => m.Text).HasMaxLength(500);
            e.Property(m => m.CreatedAt).HasColumnType("timestamptz");
            e.Property(m => m.NextAttemptAt).HasColumnType("timestamptz");
            e.HasIndex(m => m.NextAttemptAt);
        });
    }
}
