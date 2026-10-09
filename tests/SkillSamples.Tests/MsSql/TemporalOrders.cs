using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SkillSamples.MsSql;

public sealed class TemporalOrder
{
    public int Id { get; private set; }

    public required string Status { get; set; }
}

public sealed class TemporalOrderConfiguration : IEntityTypeConfiguration<TemporalOrder>
{
    public void Configure(EntityTypeBuilder<TemporalOrder> builder)
    {
        // SQL Server keeps every previous version of a row in the history table, with the period it was valid.
        builder.ToTable("orders", t => t.IsTemporal(history =>
        {
            history.HasPeriodStart("valid_from");
            history.HasPeriodEnd("valid_to");
            history.UseHistoryTable("orders_history");
        }));
        builder.Property(o => o.Id).HasColumnName("id").UseIdentityColumn();
        builder.Property(o => o.Status).HasColumnName("status").HasMaxLength(20).IsUnicode(false);
    }
}

public sealed class TemporalDbContext(DbContextOptions<TemporalDbContext> options) : DbContext(options)
{
    public DbSet<TemporalOrder> Orders => Set<TemporalOrder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfiguration(new TemporalOrderConfiguration());
}
