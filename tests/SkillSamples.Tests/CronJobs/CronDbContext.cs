using Microsoft.EntityFrameworkCore;

namespace SkillSamples.CronJobs;

public enum OrderStatus
{
    Unknown = 0,
    Placed = 1,
    Shipped = 2,
    Delivered = 3,
}

public sealed class LegacyOrder
{
    public int Id { get; init; }

    public string? LegacyStatus { get; init; }

    public OrderStatus Status { get; set; }
}

public sealed class DataMigrationRun
{
    public int Id { get; init; }

    public required string Name { get; init; }

    public int RecordsMigrated { get; init; }

    public int RecordsUnmapped { get; init; }

    public DateTimeOffset CompletedAt { get; init; }
}

public sealed class AuditEntry
{
    public int Id { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class CronDbContext(DbContextOptions<CronDbContext> options) : DbContext(options)
{
    public DbSet<LegacyOrder> Orders => Set<LegacyOrder>();

    public DbSet<DataMigrationRun> DataMigrations => Set<DataMigrationRun>();

    public DbSet<AuditEntry> AuditLog => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<LegacyOrder>(e =>
        {
            e.ToTable("cron_orders");
            e.Property(o => o.LegacyStatus).HasMaxLength(20);
        });
        model.Entity<DataMigrationRun>(e =>
        {
            e.ToTable("cron_data_migrations");
            e.Property(m => m.Name).HasMaxLength(100);
            e.HasIndex(m => m.Name).IsUnique();
            e.Property(m => m.CompletedAt).HasColumnType("timestamptz");
        });
        model.Entity<AuditEntry>(e =>
        {
            e.ToTable("cron_audit_log");
            e.Property(a => a.CreatedAt).HasColumnType("timestamptz");
        });
    }
}
