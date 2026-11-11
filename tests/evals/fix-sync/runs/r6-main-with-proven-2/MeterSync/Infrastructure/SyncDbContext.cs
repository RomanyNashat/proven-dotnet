using MeterSync.Domain;
using Microsoft.EntityFrameworkCore;

namespace MeterSync.Infrastructure;

public sealed class SyncDbContext(DbContextOptions<SyncDbContext> options) : DbContext(options)
{
    public DbSet<Patient> Patients => Set<Patient>();

    public DbSet<Meter> Meters => Set<Meter>();

    public DbSet<Reading> Readings => Set<Reading>();

    /// <summary>
    /// Inserts each reading unless one with the same vendor id is already stored, safely across instances.
    /// </summary>
    /// <returns>The number of rows inserted.</returns>
    public async Task<int> InsertIgnoringDuplicatesAsync(IEnumerable<Reading> readings, CancellationToken ct)
    {
        var inserted = 0;
        foreach (var r in readings)
        {
            // The vendor re-sends every reading on every sync. NOT EXISTS skips the known ones before the
            // identity is drawn (ON CONFLICT alone burns a key per known row each minute); ON CONFLICT
            // covers the race with another instance inserting the same reading.
            inserted += await Database.ExecuteSqlAsync(
                $"""
                INSERT INTO readings ("PatientId", "MeterId", "VendorId", "Raw", "Unit", "ValueMgDl", "TakenAt", "Alert", "AlertAttempts")
                SELECT {r.PatientId}, {r.MeterId}, {r.VendorId}, {r.Raw}, {r.Unit}, CAST({r.ValueMgDl} AS numeric(6,1)), {r.TakenAt}, {r.Alert.ToString()}, {r.AlertAttempts}
                WHERE NOT EXISTS (SELECT 1 FROM readings WHERE "VendorId" = {r.VendorId})
                ON CONFLICT ("VendorId") DO NOTHING
                """,
                ct);
        }

        return inserted;
    }

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
            e.Property(r => r.AlertClaimedUntil).HasColumnType("timestamptz");
            e.Property(r => r.Alert).HasConversion<string>().HasMaxLength(8);
            e.HasIndex(r => r.VendorId).IsUnique();
            e.HasIndex(r => r.MeterId);
            e.HasIndex(r => r.PatientId);

            // Only unsent alerts are in the index, so the sweeper's range scan on TakenAt stays small.
            e.HasIndex(r => r.TakenAt, "IX_readings_due_alerts")
                .HasFilter("\"AlertSentAt\" IS NULL AND \"Alert\" <> 'None'");

            e.HasOne<Meter>().WithMany().HasForeignKey(r => r.MeterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Patient>().WithMany().HasForeignKey(r => r.PatientId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
