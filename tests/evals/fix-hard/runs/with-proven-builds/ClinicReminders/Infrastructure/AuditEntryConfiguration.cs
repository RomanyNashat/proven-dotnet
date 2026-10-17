using ClinicReminders.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicReminders.Infrastructure;

public sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("Audit");
        builder.Property(a => a.Id).UseIdentityColumn();
        builder.Property(a => a.Action).HasMaxLength(100).IsUnicode();
        builder.Property(a => a.At).AsUtc();
    }
}
