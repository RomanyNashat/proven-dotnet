using Microsoft.EntityFrameworkCore;

namespace SkillSamples.EfCore;

/// <summary>The common mistake, kept to show it leaking: the caller is copied into a local.</summary>
public sealed class LeakyVisitsDbContext(DbContextOptions<LeakyVisitsDbContext> options) : DbContext(options)
{
    public int CallerId { get; set; }

    public DbSet<Visit> Visits => Set<Visit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new VisitConfiguration(Database.IsSqlServer() ? Engine.SqlServer : Engine.PostgreSql));
        var callerId = CallerId;
        modelBuilder.Entity<Visit>().HasQueryFilter(v => v.PatientId == callerId);
    }
}
