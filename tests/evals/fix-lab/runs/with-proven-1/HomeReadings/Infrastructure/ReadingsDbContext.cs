using HomeReadings.Domain;
using Microsoft.EntityFrameworkCore;

namespace HomeReadings.Infrastructure;

public sealed class ReadingsDbContext(DbContextOptions<ReadingsDbContext> options) : DbContext(options)
{
    public DbSet<Reading> Readings => Set<Reading>();
    public DbSet<CareTeamAlert> Alerts => Set<CareTeamAlert>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Reading>(e =>
        {
            e.ToTable("readings", t => t.HasCheckConstraint("ck_readings_value_positive", "\"Value\" > 0"));
            e.Property(r => r.Id).UseIdentityAlwaysColumn();
            e.Property(r => r.NationalId).HasMaxLength(NationalId.Length).IsRequired();
            e.Property(r => r.Value).HasPrecision(6, 2);
            e.Property(r => r.Unit).HasMaxLength(16).IsRequired();
            e.Property(r => r.OutOfRange).HasMaxLength(2);
            e.Property(r => r.CollectedAt).HasColumnType("timestamptz");
            e.Property(r => r.ReceivedAt).HasColumnType("timestamptz");
            e.Property(r => r.Notes).HasMaxLength(Reading.NotesMaxLength);
            e.Property(r => r.PhotoPath).HasMaxLength(260);
            e.HasIndex(r => new { r.PatientId, r.CollectedAt });

            // One alert per reading; the FK's unique index also serves the notifier's join.
            e.HasOne(r => r.Alert).WithOne().HasForeignKey<CareTeamAlert>(a => a.ReadingId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<CareTeamAlert>(e =>
        {
            e.ToTable("care_team_alerts");
            e.Property(a => a.Id).UseIdentityAlwaysColumn();
            e.Property(a => a.CreatedAt).HasColumnType("timestamptz");
            e.Property(a => a.SentAt).HasColumnType("timestamptz");
            e.Property(a => a.LastAttemptAt).HasColumnType("timestamptz");
            e.Property(a => a.LastError).HasMaxLength(CareTeamAlert.LastErrorMaxLength);

            // Only pending alerts: stays small however many have been sent.
            e.HasIndex(a => a.Id).HasFilter("\"SentAt\" IS NULL").HasDatabaseName("ix_care_team_alerts_pending");
        });
    }
}
