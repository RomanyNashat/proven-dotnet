using LabResults.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabResults.Infrastructure;

public sealed class LabResultConfiguration : IEntityTypeConfiguration<LabResult>
{
    /// <summary>Matches the request validator, so a long note is a 400, not a database error.</summary>
    public const int NotesMaxLength = 1000;

    public const int TestMaxLength = 40;

    public const int UnitMaxLength = 16;

    public void Configure(EntityTypeBuilder<LabResult> e)
    {
        e.ToTable("lab_results");
        e.Property(r => r.Id).UseIdentityAlwaysColumn();
        e.Property(r => r.Test).HasMaxLength(TestMaxLength);
        e.Property(r => r.Unit).HasMaxLength(UnitMaxLength);
        e.Property(r => r.Notes).HasMaxLength(NotesMaxLength);
        e.Property(r => r.Value).HasColumnType("numeric(10,3)");
        e.Property(r => r.ReceivedAt).HasColumnType("timestamptz");
        e.Property(r => r.RecordedBy).HasMaxLength(64);

        // Serves the keyset page: ORDER BY received_at DESC, id DESC. On the existing table this index goes
        // in its own reviewed script with CREATE INDEX CONCURRENTLY (see the review's migration note).
        e.HasIndex(r => new { r.ReceivedAt, r.Id }).IsDescending(true, true);
        e.HasIndex(r => r.PatientId);
    }
}
