using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SkillSamples.Postgres;

public enum OrderStatus { Pending, Paid, Cancelled }

public sealed class PgOrder
{
    public int Id { get; private set; }

    public required string Reference { get; init; }

    public OrderStatus Status { get; set; }

    public decimal Total { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public string[] Tags { get; init; } = [];
}

public sealed class OrderConfiguration : IEntityTypeConfiguration<PgOrder>
{
    public void Configure(EntityTypeBuilder<PgOrder> builder)
    {
        builder.ToTable("orders");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id").UseIdentityAlwaysColumn();               // int identity, never serial
        builder.Property(o => o.Reference).HasColumnName("reference").HasMaxLength(30);          // varchar(30)
        builder.Property(o => o.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.Total).HasColumnName("total").HasPrecision(18, 2);               // numeric(18,2)
        builder.Property(o => o.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz");
        builder.Property(o => o.Tags).HasColumnName("tags").HasColumnType("varchar(30)[]");    // string[] alone is text[]
    }
}

public sealed class PgOrdersDbContext(DbContextOptions<PgOrdersDbContext> options) : DbContext(options)
{
    public DbSet<PgOrder> Orders => Set<PgOrder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfiguration(new OrderConfiguration());
}

/// <summary>The same model with a GIN index on tags, declared in EF (a separate type: EF caches one model per type).</summary>
public sealed class PgOrdersWithGinDbContext(DbContextOptions<PgOrdersWithGinDbContext> options) : DbContext(options)
{
    public DbSet<PgOrder> Orders => Set<PgOrder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OrderConfiguration());
        modelBuilder.Entity<PgOrder>().HasIndex(o => o.Tags).HasMethod("gin");
    }
}
