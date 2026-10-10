using Microsoft.EntityFrameworkCore;

namespace SkillSamples.Compliance;

public sealed class ComplianceDbContext(DbContextOptions<ComplianceDbContext> options) : DbContext(options)
{
    public DbSet<Diagnosis> Diagnoses => Set<Diagnosis>();
    public DbSet<PhiAuditRow> PhiAudit => Set<PhiAuditRow>();
    public DbSet<Consent> Consents => Set<Consent>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Diagnosis>(d =>
        {
            d.ToTable("diagnoses");
            // HiLo: the id exists when the record is added, so its audit row can name it in the same save.
            NpgsqlPropertyBuilderExtensions.UseHiLo(d.Property(x => x.Id).HasColumnName("id"), "diagnoses_hilo");
            d.Property(x => x.PatientId).HasColumnName("patient_id");
            d.Property(x => x.Code).HasColumnName("code").HasMaxLength(10);
        });

        model.Entity<PhiAuditRow>(a =>
        {
            a.ToTable("phi_audit");
            NpgsqlPropertyBuilderExtensions.UseIdentityAlwaysColumn(a.Property(x => x.Id).HasColumnName("id"));
            a.Property(x => x.RecordType).HasColumnName("record_type").HasMaxLength(100);
            a.Property(x => x.RecordId).HasColumnName("record_id");
            a.Property(x => x.Action).HasColumnName("action").HasMaxLength(10);
            a.Property(x => x.ChangedColumns).HasColumnName("changed_columns").HasMaxLength(1000);
            a.Property(x => x.UserId).HasColumnName("user_id").HasMaxLength(100);
            a.Property(x => x.At).HasColumnName("at").HasColumnType("timestamptz");
            a.HasIndex(x => new { x.RecordType, x.RecordId });
        });

        model.Entity<Consent>(c =>
        {
            c.ToTable("consents");
            NpgsqlPropertyBuilderExtensions.UseIdentityAlwaysColumn(c.Property(x => x.Id).HasColumnName("id"));
            c.Property(x => x.SubjectId).HasColumnName("subject_id");
            c.Property(x => x.Purpose).HasColumnName("purpose").HasMaxLength(50);
            c.Property(x => x.GrantedAt).HasColumnName("granted_at").HasColumnType("timestamptz");
            c.Property(x => x.WithdrawnAt).HasColumnName("withdrawn_at").HasColumnType("timestamptz");
            c.HasIndex(x => new { x.SubjectId, x.Purpose });
        });
    }
}
