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
            e.Property(r => r.Id).UseIdentityAlwaysColumn();
            e.Property(r => r.Test).HasMaxLength(LabResultLimits.Test);
            e.Property(r => r.Unit).HasMaxLength(LabResultLimits.Unit);
            e.Property(r => r.Notes).HasMaxLength(LabResultLimits.Notes);
            e.Property(r => r.Value).HasColumnType("numeric(10,3)");
            e.Property(r => r.ReceivedAt).HasColumnType("timestamptz");
            e.Property(r => r.RecordedBy).HasMaxLength(LabResultLimits.RecordedBy);

            // Serves GET /results: one clinician's results, newest first, keyset-paged on (ReceivedAt, Id).
            e.HasIndex(r => new { r.RecordedBy, r.ReceivedAt, r.Id });
            e.HasIndex(r => r.PatientId);
        });
    }
}
