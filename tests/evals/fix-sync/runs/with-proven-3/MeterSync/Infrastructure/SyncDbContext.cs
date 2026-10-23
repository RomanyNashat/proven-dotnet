using MeterSync.Domain;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Infrastructure;

public sealed class SyncDbContext(DbContextOptions<SyncDbContext> options) : DbContext(options)
{
    /// <summary>Upper-case serial, filled by the database. Serials are unique whatever their case.</summary>
    public const string SerialKey = nameof(SerialKey);

    public DbSet<Patient> Patients => Set<Patient>();

    public DbSet<Meter> Meters => Set<Meter>();

    public DbSet<Reading> Readings => Set<Reading>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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
            e.Property(m => m.SerialNumber).HasMaxLength(40);
            e.Property<string>(SerialKey).HasMaxLength(40).HasComputedColumnSql("upper(\"SerialNumber\")", stored: true);
            e.Property(m => m.LinkedAt).HasColumnType("timestamptz");
            e.HasIndex(SerialKey).IsUnique();
            e.HasIndex(m => m.SerialNumber).IsUnique();
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
            e.Property(r => r.AlertLastError).HasMaxLength(Reading.LastErrorMaxLength);
            e.Property(r => r.Alert).HasConversion<string>().HasMaxLength(8);
            e.Property(r => r.AlertDelivery).HasConversion<string>().HasMaxLength(10);
            e.HasIndex(r => r.VendorId).IsUnique();

            // What the sweeper claims from. Only pending alerts are in it, so it stays small.
            e.HasIndex(r => r.Id, "ix_readings_pending_alerts").HasFilter("\"AlertDelivery\" = 'Pending'");

            // Health data stays when a patient or meter row goes: deleting one with readings is refused.
            e.HasOne<Meter>().WithMany().HasForeignKey(r => r.MeterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Patient>().WithMany().HasForeignKey(r => r.PatientId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
