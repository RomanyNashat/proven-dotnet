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
            e.Ignore(r => r.ValueInMmolPerL);
            e.Ignore(r => r.NeedsAlert);

            // The same reading sent twice (the app retried after a dropped response) is one reading,
            // and so one alert. Leads with (PatientId, CollectedAt), so it also serves the patient's queries.
            e.HasIndex(r => new { r.PatientId, r.CollectedAt, r.Value, r.Unit }).IsUnique();
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
            e.HasIndex(a => new { a.SentAt, a.ClaimedAt });
        });
    }
}
