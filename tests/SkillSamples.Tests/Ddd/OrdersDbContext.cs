using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SkillSamples.Ddd;

public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var sqlServer = Database.IsSqlServer();
        modelBuilder.ApplyConfiguration(new OrderConfiguration(sqlServer));
        modelBuilder.ApplyConfiguration(new OrderLineConfiguration(sqlServer));
    }
}

/// <summary>A service has one engine and keeps one branch; both are here so CI checks both.</summary>
public sealed class OrderConfiguration(bool sqlServer) : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");
        // An int, known at AddAsync, before SaveChanges. With one provider this is .UseHiLo("orders_hilo");
        // the samples project references both, so each call names its provider.
        var id = builder.Property(o => o.Id);
        if (sqlServer)
        {
            SqlServerPropertyBuilderExtensions.UseHiLo(id, "orders_hilo");
        }
        else
        {
            NpgsqlPropertyBuilderExtensions.UseHiLo(id, "orders_hilo");
        }

        builder.ComplexProperty(o => o.Total, money =>
        {
            money.Property(m => m.Amount).HasColumnName("total_amount").HasPrecision(18, 2);
            money.Property(m => m.Currency).HasColumnName("total_currency").HasMaxLength(3).IsUnicode(false);
        });
        builder.HasMany(o => o.Lines).WithOne().HasForeignKey("OrderId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(o => o.CustomerId);
    }
}

public sealed class OrderLineConfiguration(bool sqlServer) : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        builder.ToTable("order_lines");
        if (sqlServer)
        {
            builder.Property(l => l.Id).UseIdentityColumn();
        }
        else
        {
            builder.Property(l => l.Id).UseIdentityAlwaysColumn();
        }

        builder.Property(l => l.ProductName).HasMaxLength(200).IsRequired();
        builder.ComplexProperty(l => l.UnitPrice, money =>
        {
            money.Property(m => m.Amount).HasColumnName("unit_price_amount").HasPrecision(18, 2);
            money.Property(m => m.Currency).HasColumnName("unit_price_currency").HasMaxLength(3).IsUnicode(false);
        });
        builder.Ignore(l => l.Subtotal);
    }
}
