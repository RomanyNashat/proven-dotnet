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
        // GENERATED ALWAYS AS IDENTITY; Npgsql's default is BY DEFAULT, which lets an INSERT pick its own id.
        modelBuilder.UseIdentityAlwaysColumns();

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
            e.Property(m => m.SerialNumber).HasMaxLength(Meter.SerialNumberMaxLength);
            e.Property(m => m.SerialKey).HasMaxLength(Meter.SerialNumberMaxLength);
            e.Property(m => m.LinkedAt).HasColumnType("timestamptz");
            e.HasIndex(m => m.SerialKey).IsUnique();
            e.HasOne<Patient>().WithMany().HasForeignKey(m => m.PatientId).OnDelete(DeleteBehavior.Restrict);
        });

        // ReadingWriter and AlertClaims write these column names by hand (SyncDbContextTests checks them).
        modelBuilder.Entity<Reading>(e =>
        {
            e.ToTable("readings");
            e.Property(r => r.VendorId).HasMaxLength(Reading.VendorIdMaxLength);
            e.Property(r => r.Raw).HasMaxLength(Reading.RawMaxLength);
            e.Property(r => r.Unit).HasMaxLength(Reading.UnitMaxLength);
            e.Property(r => r.ValueMgDl).HasColumnType("numeric(6,1)");
            e.Property(r => r.TakenAt).HasColumnType("timestamptz");
            e.Property(r => r.Alert).HasConversion<string>().HasMaxLength(8);
            e.Property(r => r.AlertDelivery).HasConversion<string>().HasMaxLength(10);
            e.Property(r => r.AlertClaimedUntil).HasColumnType("timestamptz");
            e.Property(r => r.AlertSentAt).HasColumnType("timestamptz");
            e.Property(r => r.AlertFailedAt).HasColumnType("timestamptz");
            e.HasIndex(r => r.VendorId).IsUnique();

            // The sweeper's query: pending alerts, oldest first. Small, because sent rows leave it.
            e.HasIndex(r => r.Id, "ix_readings_alert_pending").HasFilter("\"AlertDelivery\" = 'Pending'");

            // The foreign keys also give MeterId and PatientId their indexes.
            e.HasOne<Meter>().WithMany().HasForeignKey(r => r.MeterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Patient>().WithMany().HasForeignKey(r => r.PatientId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
