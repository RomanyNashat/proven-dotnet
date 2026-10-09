using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace SkillSamples.Postgres;

public sealed class Product
{
    public int Id { get; private set; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public NpgsqlTsVector SearchVector { get; private set; } = null!;
}

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");
        builder.Property(p => p.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        builder.Property(p => p.Name).HasColumnName("name").HasMaxLength(200);
        builder.Property(p => p.Description).HasColumnName("description").HasMaxLength(1000);

        // 'simple' splits words without stemming: right for Arabic and mixed text ('english' stems English only).
        builder.Property(p => p.SearchVector)
            .HasColumnName("search_vector")
            .HasColumnType("tsvector")
            .HasComputedColumnSql("to_tsvector('simple', coalesce(name, '') || ' ' || coalesce(description, ''))", stored: true);
        builder.HasIndex(p => p.SearchVector).HasMethod("GIN");
    }
}

public sealed class PgProductsDbContext(DbContextOptions<PgProductsDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfiguration(new ProductConfiguration());

    public Task<List<string>> SearchAsync(string term, CancellationToken ct) =>
        Products
            .Where(p => p.SearchVector.Matches(EF.Functions.PlainToTsQuery("simple", term)))
            .OrderByDescending(p => p.SearchVector.Rank(EF.Functions.PlainToTsQuery("simple", term)))
            .Select(p => p.Name)
            .Take(20)
            .ToListAsync(ct);
}
