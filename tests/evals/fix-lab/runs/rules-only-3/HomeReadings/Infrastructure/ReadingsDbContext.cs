using HomeReadings.Domain;
using Microsoft.EntityFrameworkCore;

namespace HomeReadings.Infrastructure;

public sealed class ReadingsDbContext(DbContextOptions<ReadingsDbContext> options) : DbContext(options)
{
    public const int NotesMaxLength = 500;

    public DbSet<Reading> Readings => Set<Reading>();
    public DbSet<CareTeamAlert> Alerts => Set<CareTeamAlert>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Reading>(e =>
        {
            e.ToTable("readings");
            e.Property(r => r.Id).UseIdentityAlwaysColumn();
            e.Property(r => r.NationalId).HasMaxLength(10).IsRequired();
            e.Property(r => r.Value).HasPrecision(6, 2);
            e.Property(r => r.Unit).HasMaxLength(16).IsRequired();
            e.Property(r => r.CollectedAt).HasColumnType("timestamptz");
            e.Property(r => r.ReceivedAt).HasColumnType("timestamptz");
            e.Property(r => r.Notes).HasMaxLength(NotesMaxLength);
            e.Property(r => r.PhotoPath).HasMaxLength(260);

            // A meter reading is identified by when it was taken and what it showed. The app retries a
            // POST whose answer it lost; this makes the retry the same reading, not a second one (and not
            // a second alert to a nurse). It also serves the per-patient queries (PatientId, CollectedAt).
            e.HasIndex(r => new { r.PatientId, r.CollectedAt, r.Value }).IsUnique();
        });
        modelBuilder.Entity<CareTeamAlert>(e =>
        {
            e.ToTable("care_team_alerts");
            e.Property(a => a.Id).UseIdentityAlwaysColumn();
            e.Property(a => a.CreatedAt).HasColumnType("timestamptz");
            e.Property(a => a.ClaimedAt).HasColumnType("timestamptz");
            e.Property(a => a.SentAt).HasColumnType("timestamptz");
            e.HasOne(a => a.Reading).WithMany().HasForeignKey(a => a.ReadingId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(a => a.ReadingId).IsUnique();
            e.HasIndex(a => a.Id).HasFilter("\"SentAt\" IS NULL").HasDatabaseName("ix_care_team_alerts_waiting");
        });
    }
}
