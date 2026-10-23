using MeterSync.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MeterSync.Infrastructure.Configurations;

internal sealed class ReadingConfiguration : IEntityTypeConfiguration<Reading>
{
    public void Configure(EntityTypeBuilder<Reading> e)
    {
        e.ToTable("readings");
        e.Property(r => r.VendorId).HasMaxLength(Reading.VendorIdMaxLength);
        e.Property(r => r.Raw).HasMaxLength(Reading.RawMaxLength);
        e.Property(r => r.Unit).HasMaxLength(Reading.UnitMaxLength);
        e.Property(r => r.ValueMgDl).HasColumnType("numeric(6,1)");
        e.Property(r => r.TakenAt).HasColumnType("timestamptz");
        e.Property(r => r.AlertClaimedUntil).HasColumnType("timestamptz");
        e.Property(r => r.AlertSentAt).HasColumnType("timestamptz");
        e.Property(r => r.Alert).HasConversion<string>().HasMaxLength(8);
        e.Property(r => r.Status).HasConversion<string>().HasMaxLength(16);

        // ReadingInsert's ON CONFLICT relies on this index.
        e.HasIndex(r => r.VendorId).IsUnique();

        // Only the alerts still to send: small, whatever the table grows to.
        e.HasIndex(r => r.Id, "ix_readings_alert_due").HasFilter("\"Status\" = 'Pending'");

        e.HasOne<Meter>().WithMany().HasForeignKey(r => r.MeterId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Patient>().WithMany().HasForeignKey(r => r.PatientId).OnDelete(DeleteBehavior.Restrict);
    }
}
