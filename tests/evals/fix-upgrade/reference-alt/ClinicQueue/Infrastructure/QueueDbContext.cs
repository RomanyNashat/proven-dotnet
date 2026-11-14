using ClinicQueue.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClinicQueue.Infrastructure;

public sealed class QueueDbContext(DbContextOptions<QueueDbContext> options) : DbContext(options)
{
    public DbSet<Clinic> Clinics => Set<Clinic>();
    public DbSet<Visit> Visits => Set<Visit>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Clinic>(e =>
        {
            e.ToTable("clinic");
            e.Property(c => c.Id).HasColumnName("id");
            e.Property(c => c.Name).HasColumnName("name").HasMaxLength(100);
        });
        model.Entity<Visit>(e =>
        {
            e.ToTable("visit");
            e.Property(v => v.Id).HasColumnName("id");
            e.Property(v => v.ClinicId).HasColumnName("clinic_id");
            e.Property(v => v.PatientId).HasColumnName("patient_id");
            e.Property(v => v.Day).HasColumnName("day");
            e.Property(v => v.Position).HasColumnName("position");
            e.HasIndex(v => new { v.ClinicId, v.Day });
        });
    }
}
