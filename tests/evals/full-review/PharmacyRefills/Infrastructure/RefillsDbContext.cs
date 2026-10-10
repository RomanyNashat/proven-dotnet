using Microsoft.EntityFrameworkCore;
using PharmacyRefills.Domain;

namespace PharmacyRefills.Infrastructure;

public sealed class RefillsDbContext(DbContextOptions<RefillsDbContext> options) : DbContext(options)
{
    public DbSet<Refill> Refills => Set<Refill>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Refill>(r =>
        {
            r.ToTable("refills");
            r.Property(x => x.MedicationName).HasMaxLength(200);
            r.Property(x => x.RequestedAt).HasColumnType("timestamptz");
            r.HasIndex(x => x.PatientId);
        });
    }
}
