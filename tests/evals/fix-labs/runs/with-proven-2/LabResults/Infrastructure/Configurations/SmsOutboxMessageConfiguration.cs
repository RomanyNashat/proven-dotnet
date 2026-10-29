using LabResults.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabResults.Infrastructure.Configurations;

internal sealed class SmsOutboxMessageConfiguration : IEntityTypeConfiguration<SmsOutboxMessage>
{
    public void Configure(EntityTypeBuilder<SmsOutboxMessage> builder)
    {
        builder.ToTable("sms_outbox");
        builder.Property(m => m.Id).UseIdentityAlwaysColumn();
        builder.Property(m => m.Phone).HasMaxLength(16);
        builder.Property(m => m.CreatedAt).HasColumnType("timestamptz");
        builder.Property(m => m.LastError).HasMaxLength(100);
        builder.HasIndex(m => new { m.Attempts, m.Id }).HasDatabaseName("ix_sms_outbox_attempts_id");
    }
}
