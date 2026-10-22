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
            e.Property(r => r.NationalId).HasMaxLength(10).IsRequired();
            e.Property(r => r.Value).HasPrecision(6, 2);
            e.Property(r => r.Unit).HasMaxLength(16).IsRequired();
            e.Property(r => r.CollectedAt).HasColumnType("timestamptz");
            e.Property(r => r.ReceivedAt).HasColumnType("timestamptz");
            e.Property(r => r.PhotoPath).HasMaxLength(260);
            e.HasIndex(r => new { r.PatientId, r.CollectedAt });
        });
        modelBuilder.Entity<CareTeamAlert>(e =>
        {
            e.ToTable("care_team_alerts");
            e.Property(a => a.Id).UseIdentityAlwaysColumn();
            e.Property(a => a.CreatedAt).HasColumnType("timestamptz");
            e.Property(a => a.SentAt).HasColumnType("timestamptz");
            e.HasIndex(a => a.SentAt);
            e.HasOne<Reading>().WithMany().HasForeignKey(a => a.ReadingId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
