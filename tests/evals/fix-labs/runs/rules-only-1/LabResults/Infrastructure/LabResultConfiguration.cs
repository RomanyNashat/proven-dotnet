using LabResults.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabResults.Infrastructure;

public sealed class LabResultConfiguration : IEntityTypeConfiguration<LabResult>
{
    public void Configure(EntityTypeBuilder<LabResult> e)
    {
        e.ToTable("lab_results");
        e.HasKey(r => r.Id);
        e.Property(r => r.Id).UseIdentityAlwaysColumn();
        e.Property(r => r.PatientId).HasColumnType("integer");
        e.Property(r => r.Test).HasMaxLength(40);
        e.Property(r => r.Unit).HasMaxLength(16);

        // Unconfigured, this was an unbounded text column.
        e.Property(r => r.Notes).HasMaxLength(1000);
        e.Property(r => r.Value).HasColumnType("numeric(10,3)");
        e.Property(r => r.ReceivedAt).HasColumnType("timestamptz");
        e.Property(r => r.RecordedBy).HasMaxLength(64);

        // Serves the list: one clinician's results, newest first, keyset on (ReceivedAt, Id).
        e.HasIndex(r => new { r.RecordedBy, r.ReceivedAt, r.Id }).IsDescending(false, true, true);
        e.HasIndex(r => r.PatientId);
    }
}
