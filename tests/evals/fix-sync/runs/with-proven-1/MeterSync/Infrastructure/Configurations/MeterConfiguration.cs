using MeterSync.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MeterSync.Infrastructure.Configurations;

internal sealed class MeterConfiguration : IEntityTypeConfiguration<Meter>
{
    public void Configure(EntityTypeBuilder<Meter> e)
    {
        e.ToTable("meters");
        e.Property(m => m.SerialNumber).HasMaxLength(40);
        e.Property(m => m.LinkedAt).HasColumnType("timestamptz");
        e.HasIndex(m => m.SerialNumber).IsUnique();
        e.HasIndex(m => m.PatientId);

        // Restrict, not cascade: deleting a patient must not silently delete their health records.
        e.HasOne<Patient>().WithMany().HasForeignKey(m => m.PatientId).OnDelete(DeleteBehavior.Restrict);
    }
}
