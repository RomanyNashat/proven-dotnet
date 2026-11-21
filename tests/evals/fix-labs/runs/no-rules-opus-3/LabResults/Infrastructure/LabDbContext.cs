using LabResults.Domain;
using Microsoft.EntityFrameworkCore;

namespace LabResults.Infrastructure;

public sealed class LabDbContext(DbContextOptions<LabDbContext> options) : DbContext(options)
{
    public DbSet<LabResult> Results => Set<LabResult>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LabResult>(e =>
        {
            e.ToTable("lab_results");
            e.Property(r => r.Test).HasMaxLength(LabResult.MaxTestLength);
            e.Property(r => r.Unit).HasMaxLength(LabResult.MaxUnitLength);
            e.Property(r => r.Notes).HasMaxLength(LabResult.MaxNotesLength);
            e.Property(r => r.Value).HasColumnType("numeric(10,3)");
            e.Property(r => r.ReceivedAt).HasColumnType("timestamptz");
            e.Property(r => r.RecordedBy).HasMaxLength(LabResult.MaxRecordedByLength);

            // Keyset pagination orders by (ReceivedAt, Id); Id breaks ties between results received at the same instant.
            e.HasIndex(r => new { r.ReceivedAt, r.Id });
            e.HasIndex(r => r.PatientId);
        });
    }
}
