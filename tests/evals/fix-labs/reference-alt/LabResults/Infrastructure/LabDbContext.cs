using LabResults.Domain;
using Microsoft.EntityFrameworkCore;

namespace LabResults.Infrastructure;

public sealed class LabDbContext(DbContextOptions<LabDbContext> options) : DbContext(options)
{
    public DbSet<LabResult> Results => Set<LabResult>();

    public DbSet<PendingSms> Sms => Set<PendingSms>();

    /// <summary>The caller for this rental, set by the scoped registration (a pooled context outlives requests).</summary>
    public string CallerId { get; set; } = "system";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LabResult>(e =>
        {
            e.ToTable("lab_results");
            e.Property(r => r.Test).HasMaxLength(40);
            e.Property(r => r.Unit).HasMaxLength(16);
            e.Property(r => r.Value).HasColumnType("numeric(10,3)");
            e.Property(r => r.ReceivedAt).HasColumnType("timestamptz");
            e.Property(r => r.RecordedBy).HasMaxLength(64);
            e.Property(r => r.Notes).HasMaxLength(4000);
            modelBuilder.Entity<PendingSms>(p =>
            {
                p.ToTable("pending_sms");
                p.Property(s => s.To).HasMaxLength(20);
                p.Property(s => s.Text).HasMaxLength(400);
                p.Property(s => s.SentAt).HasColumnType("timestamptz");
                p.HasIndex(s => s.SentAt);
            });
            e.HasIndex(r => r.ReceivedAt);
            e.HasIndex(r => r.PatientId);
        });
    }
}
