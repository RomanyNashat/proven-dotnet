using Microsoft.EntityFrameworkCore;

namespace SkillSamples.Localization;

public sealed class HospitalsDbContext(DbContextOptions<HospitalsDbContext> options) : DbContext(options)
{
    public DbSet<Hospital> Hospitals => Set<Hospital>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfiguration(new HospitalConfiguration(Database.IsSqlServer()));
}
