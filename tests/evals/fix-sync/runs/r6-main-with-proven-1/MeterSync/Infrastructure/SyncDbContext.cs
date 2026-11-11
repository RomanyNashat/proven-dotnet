using MeterSync.Domain;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Infrastructure;

/// <summary>The service's database. The schema is applied from reviewed scripts, never by the app.</summary>
public sealed class SyncDbContext(DbContextOptions<SyncDbContext> options) : DbContext(options)
{
    // The alert sweeper's raw SQL repeats this predicate, so the planner can use the partial index.
    private const string AlertDueFilter = "\"Alert\" <> 'None' AND \"AlertSentAt\" IS NULL";

    /// <summary>Registered patients.</summary>
    public DbSet<Patient> Patients => Set<Patient>();

    /// <summary>Meters linked to patients.</summary>
    public DbSet<Meter> Meters => Set<Meter>();

    /// <summary>Readings pulled from the vendor.</summary>
    public DbSet<Reading> Readings => Set<Reading>();

    /// <inheritdoc />
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
            e.Property(r => r.Alert).HasConversion<string>().HasMaxLength(8);
            e.Property(r => r.AlertFailures).HasDefaultValue(0);
            e.HasIndex(r => r.VendorId).IsUnique();
            e.HasIndex(r => r.Id).HasFilter(AlertDueFilter).HasDatabaseName("IX_readings_alert_due");
            e.HasOne<Meter>().WithMany().HasForeignKey(r => r.MeterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Patient>().WithMany().HasForeignKey(r => r.PatientId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
