namespace Clinic.Api;

public sealed class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> builder)
    {
        builder.Property(p => p.NameAr).HasMaxLength(100);
        builder.Property(p => p.Code).HasMaxLength(10).IsUnicode(false);
    }
}
