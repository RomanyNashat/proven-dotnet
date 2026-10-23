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
        // Keys are GENERATED ALWAYS AS IDENTITY, never serial or BY DEFAULT.
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
            e.Property(r => r.VendorId).HasMaxLength(64);
            e.Property(r => r.Raw).HasMaxLength(16);
            e.Property(r => r.Unit).HasMaxLength(8);
            e.Property(r => r.ValueMgDl).HasColumnType("numeric(6,1)");
            e.Property(r => r.TakenAt).HasColumnType("timestamptz");
            e.Property(r => r.AlertSentAt).HasColumnType("timestamptz");
            e.Property(r => r.AlertClaimedUntil).HasColumnType("timestamptz");
            e.Property(r => r.Alert).HasConversion<string>().HasMaxLength(8);
            e.HasIndex(r => r.VendorId).IsUnique();
            e.HasIndex(r => r.MeterId);
            e.HasIndex(r => new { r.Alert, r.AlertSentAt });
            e.HasOne<Patient>().WithMany().HasForeignKey(r => r.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Meter>().WithMany().HasForeignKey(r => r.MeterId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
