using MeterSync.Domain;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Infrastructure;

public sealed class SyncDbContext(DbContextOptions<SyncDbContext> options) : DbContext(options)
{
    public DbSet<Patient> Patients => Set<Patient>();

    public DbSet<Meter> Meters => Set<Meter>();

    public DbSet<Reading> Readings => Set<Reading>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Patient>(e =>
        {
            e.ToTable("patients");
            e.Property(p => p.NationalId).HasMaxLength(10);
            e.Property(p => p.Phone).HasMaxLength(20);
            e.HasIndex(p => p.NationalId).IsUnique();
        });

        modelBuilder.Entity<Meter>(e =>
        {
            e.ToTable("meters");
            e.Property(m => m.SerialNumber).HasMaxLength(40);
            e.Property(m => m.LinkedAt).HasColumnType("timestamptz");
            e.HasIndex(m => m.SerialNumber).IsUnique();
            e.HasIndex(m => m.PatientId);
            e.HasOne<Patient>().WithMany().HasForeignKey(m => m.PatientId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Reading>(e =>
        {
            e.ToTable("readings");
            e.Property(r => r.VendorId).HasMaxLength(Reading.VendorIdMaxLength);
            e.Property(r => r.Raw).HasMaxLength(Reading.RawMaxLength);
            e.Property(r => r.Unit).HasMaxLength(Reading.UnitMaxLength);
            e.Property(r => r.ValueMgDl).HasColumnType("numeric(6,1)");
            e.Property(r => r.TakenAt).HasColumnType("timestamptz");
            e.Property(r => r.AlertSentAt).HasColumnType("timestamptz");
            e.Property(r => r.AlertClaimedUntil).HasColumnType("timestamptz");
            e.Property(r => r.Alert).HasConversion<string>().HasMaxLength(8);
            e.HasIndex(r => r.VendorId).IsUnique();
            e.HasIndex(r => new { r.MeterId, r.TakenAt });

            // The sweeper's candidates: out-of-range readings not sent yet. Columns are PascalCase and quoted
            // (no naming convention in this model); the filter has to match Reading.DueForAlert for the planner.
            e.HasIndex(r => r.TakenAt)
                .HasDatabaseName("IX_readings_alert_due")
                .HasFilter("\"AlertSentAt\" IS NULL AND \"Alert\" <> 'None'");

            e.HasOne<Meter>().WithMany().HasForeignKey(r => r.MeterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Patient>().WithMany().HasForeignKey(r => r.PatientId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
