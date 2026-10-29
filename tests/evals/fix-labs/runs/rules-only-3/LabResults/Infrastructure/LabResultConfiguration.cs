using LabResults.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabResults.Infrastructure;

public sealed class LabResultConfiguration : IEntityTypeConfiguration<LabResult>
{
    public void Configure(EntityTypeBuilder<LabResult> e)
    {
        e.ToTable("lab_results");
        e.Property(r => r.Id).UseIdentityAlwaysColumn();
        e.Property(r => r.Test).HasMaxLength(LabResult.TestMaxLength);
        e.Property(r => r.Unit).HasMaxLength(LabResult.UnitMaxLength);
        e.Property(r => r.Notes).HasMaxLength(LabResult.NotesMaxLength);
        e.Property(r => r.Value).HasColumnType("numeric(10,3)");
        e.Property(r => r.ReceivedAt).HasColumnType("timestamptz");
        e.Property(r => r.RecordedBy).HasMaxLength(LabResult.RecordedByMaxLength);

        // Serves the clinician's newest-first page: filter on RecordedBy, seek on (ReceivedAt, Id).
        e.HasIndex(r => new { r.RecordedBy, r.ReceivedAt, r.Id }).IsDescending(false, true, true);
        e.HasIndex(r => r.PatientId);
    }
}
