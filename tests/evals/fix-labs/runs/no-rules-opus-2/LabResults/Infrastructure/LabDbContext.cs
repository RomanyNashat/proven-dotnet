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
            e.Property(r => r.Test).HasMaxLength(40);
            e.Property(r => r.Unit).HasMaxLength(16);
            e.Property(r => r.Value).HasColumnType("numeric(10,3)");
            e.Property(r => r.ReceivedAt).HasColumnType("timestamptz");
            e.Property(r => r.Notes).HasMaxLength(2000);
            e.Property(r => r.RecordedBy).HasMaxLength(64);
            // Matches the keyset pagination order (newest first, id as tie-breaker).
            e.HasIndex(r => new { r.ReceivedAt, r.Id }).IsDescending();
            e.HasIndex(r => new { r.PatientId, r.ReceivedAt, r.Id }).IsDescending(false, true, true);
        });
    }
}
