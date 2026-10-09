using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SkillSamples.EfCore;

public enum Engine { PostgreSql, SqlServer }

/// <summary>
/// The same entity under the column rules on each engine. A real service has one engine and keeps one
/// branch; both are here so CI checks both.
/// </summary>
public sealed class VisitConfiguration(Engine engine) : IEntityTypeConfiguration<Visit>
{
    /// <summary>The concurrency token, a shadow property: its type differs by engine (see VersionToken).</summary>
    public const string Version = nameof(Version);

    public void Configure(EntityTypeBuilder<Visit> builder)
    {
        builder.ToTable("visits");
        builder.Property(v => v.Notes).HasMaxLength(500).IsUnicode().IsRequired();   // no length = text / nvarchar(max)
        builder.HasIndex(v => v.PatientId);

        if (engine == Engine.PostgreSql)
        {
            builder.Property(v => v.Id).UseIdentityAlwaysColumn();
            builder.Property(v => v.CreatedAt).HasColumnType("timestamptz");
            builder.Property(v => v.UpdatedAt).HasColumnType("timestamptz");
            builder.Property<uint>(Version).IsRowVersion();          // PostgreSQL's xmin: no column added
        }
        else
        {
            builder.Property(v => v.Id).UseIdentityColumn();
            // datetime2(3) in UTC. Without the converter EF maps DateTimeOffset to datetimeoffset.
            builder.Property(v => v.CreatedAt).HasColumnType("datetime2(3)")
                .HasConversion(v => v.UtcDateTime, v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));
            builder.Property(v => v.UpdatedAt).HasColumnType("datetime2(3)")
                .HasConversion(v => v!.Value.UtcDateTime, v => (DateTimeOffset?)new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));
            builder.Property<byte[]>(Version).IsRowVersion();        // a rowversion column
        }
    }
}
