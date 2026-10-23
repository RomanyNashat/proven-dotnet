using MeterSync.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MeterSync.Infrastructure.Configurations;

internal sealed class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> e)
    {
        e.ToTable("patients");
        e.Property(p => p.NationalId).HasMaxLength(10);
        e.Property(p => p.Phone).HasMaxLength(20);
        e.HasIndex(p => p.NationalId).IsUnique();
    }
}
