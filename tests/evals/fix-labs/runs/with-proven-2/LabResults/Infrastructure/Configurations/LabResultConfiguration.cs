using LabResults.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabResults.Infrastructure.Configurations;

internal sealed class LabResultConfiguration : IEntityTypeConfiguration<LabResult>
{
    public void Configure(EntityTypeBuilder<LabResult> builder)
    {
        builder.ToTable("lab_results");
        builder.Property(r => r.Id).UseIdentityAlwaysColumn();
        builder.Property(r => r.Test).HasMaxLength(40);
        builder.Property(r => r.Unit).HasMaxLength(16);
        builder.Property(r => r.Value).HasColumnType("numeric(10,3)");
        builder.Property(r => r.Notes).HasMaxLength(1000);
        builder.Property(r => r.ReceivedAt).HasColumnType("timestamptz");
        builder.Property(r => r.RecordedBy).HasMaxLength(64);

        // Serves GET /results: the caller's rows, newest first, with the id as the tie-breaker of the cursor.
        builder.HasIndex(r => new { r.RecordedBy, r.ReceivedAt, r.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_lab_results_recorded_by_received_at_id");
        builder.HasIndex(r => r.PatientId);
    }
}
