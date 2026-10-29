using LabResults.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabResults.Infrastructure.Configurations;

internal sealed class AccessAuditEntryConfiguration : IEntityTypeConfiguration<AccessAuditEntry>
{
    public void Configure(EntityTypeBuilder<AccessAuditEntry> builder)
    {
        builder.ToTable("access_audit");
        builder.Property(a => a.Id).UseIdentityAlwaysColumn();
        builder.Property(a => a.UserId).HasMaxLength(64);
        builder.Property(a => a.Action).HasMaxLength(32);
        builder.Property(a => a.PatientRef).HasMaxLength(32);
        builder.Property(a => a.At).HasColumnType("timestamptz");
        builder.HasIndex(a => new { a.UserId, a.At }).HasDatabaseName("ix_access_audit_user_id_at");
    }
}
