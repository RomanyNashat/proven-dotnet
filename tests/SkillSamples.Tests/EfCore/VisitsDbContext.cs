using Microsoft.EntityFrameworkCore;

namespace SkillSamples.EfCore;

public sealed class VisitsDbContext(DbContextOptions<VisitsDbContext> options) : DbContext(options)
{
    public const string OwnerFilter = nameof(OwnerFilter);
    public const string SoftDeleteFilter = nameof(SoftDeleteFilter);

    /// <summary>
    /// Set on every rental (see <see cref="VisitsRegistration"/>). The filter reads it from the context
    /// instance, so EF sends it as a parameter on each query. A value copied into a local inside
    /// OnModelCreating is baked into the model, which EF builds once per context type: every caller would
    /// then get the first caller's rows.
    /// </summary>
    public int CallerId { get; set; }

    public DbSet<Visit> Visits => Set<Visit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new VisitConfiguration(Database.IsSqlServer() ? Engine.SqlServer : Engine.PostgreSql));

        // Filters that read the context live here; the rest of the mapping stays in the configuration class.
        modelBuilder.Entity<Visit>()
            .HasQueryFilter(OwnerFilter, v => v.PatientId == CallerId)
            .HasQueryFilter(SoftDeleteFilter, v => !v.IsDeleted);
    }
}
