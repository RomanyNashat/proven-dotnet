using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SkillSamples.Localization;

// A service has one engine and keeps one branch; both are here so CI checks both.
public sealed class HospitalConfiguration(bool sqlServer) : IEntityTypeConfiguration<Hospital>
{
    public void Configure(EntityTypeBuilder<Hospital> builder)
    {
        builder.ToTable("hospitals");
        var id = builder.Property(h => h.Id).HasColumnName("id");
        if (sqlServer)
        {
            id.UseIdentityColumn();
        }
        else
        {
            id.UseIdentityAlwaysColumn();
        }

        // IsUnicode() makes nvarchar(n) on SQL Server. A varchar column there stores Arabic as "???" (tested),
        // with no error. PostgreSQL's varchar(n) is UTF-8 and ignores IsUnicode.
        builder.Property(h => h.NameAr).HasColumnName("name_ar").HasMaxLength(200).IsUnicode();
        builder.Property(h => h.NameEn).HasColumnName("name_en").HasMaxLength(200).IsUnicode();
        builder.Property(h => h.NameArSearch).HasColumnName("name_ar_search").HasMaxLength(200).IsUnicode();

        // Prefix search. PostgreSQL needs varchar_pattern_ops for LIKE 'x%' unless the database collation is C;
        // on SQL Server a plain index serves it.
        var search = builder.HasIndex(h => h.NameArSearch).HasDatabaseName("ix_hospitals_name_ar_search");
        if (!sqlServer)
        {
            search.HasOperators("varchar_pattern_ops");
        }
    }
}
