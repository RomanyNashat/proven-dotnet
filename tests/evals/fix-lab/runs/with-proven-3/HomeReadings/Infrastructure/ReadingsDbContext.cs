using HomeReadings.Application;
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
            e.ToTable("readings");
            e.Property(r => r.Id).UseIdentityAlwaysColumn();
            e.Property(r => r.NationalId).HasMaxLength(NewReadingValidator.NationalIdLength).IsRequired();
            e.Property(r => r.Value).HasPrecision(6, 2);
            e.Property(r => r.OutOfRange).HasConversion<string>().HasMaxLength(8);
            e.Property(r => r.Unit).HasMaxLength(GlucoseUnit.MaxLength).IsRequired();
            e.Property(r => r.CollectedAt).HasColumnType("timestamptz");
            e.Property(r => r.ReceivedAt).HasColumnType("timestamptz");
            e.Property(r => r.Notes).HasMaxLength(NewReadingValidator.NotesMaxLength);
            e.Property(r => r.PhotoPath).HasMaxLength(64);
            e.Ignore(r => r.ValueInMmolPerLitre);
            e.Ignore(r => r.NeedsAlert);
            e.HasIndex(r => new { r.PatientId, r.CollectedAt });
        });
        modelBuilder.Entity<CareTeamAlert>(e =>
        {
            e.ToTable("care_team_alerts");
            e.Property(a => a.Id).UseIdentityAlwaysColumn();
            e.Property(a => a.CreatedAt).HasColumnType("timestamptz");
            e.Property(a => a.SentAt).HasColumnType("timestamptz");
            e.Property(a => a.SendingStartedAt).HasColumnType("timestamptz");
            e.Property(a => a.NeedsFollowUpAt).HasColumnType("timestamptz");
            e.Property(a => a.LastError).HasMaxLength(CareTeamAlert.LastErrorMaxLength);
            e.HasOne(a => a.Reading).WithMany().HasForeignKey(a => a.ReadingId).OnDelete(DeleteBehavior.Restrict);

            // Serves the notifier's "waiting" query; stays small however many alerts have been sent.
            e.HasIndex(a => a.Id)
                .HasFilter("\"SentAt\" IS NULL AND \"NeedsFollowUpAt\" IS NULL")
                .HasDatabaseName("ix_care_team_alerts_waiting");
        });
    }
}
