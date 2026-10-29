using LabResults.Domain;
using Microsoft.EntityFrameworkCore;

namespace LabResults.Infrastructure;

public sealed class LabDbContext(DbContextOptions<LabDbContext> options) : DbContext(options)
{
    /// <summary>
    /// The caller's <c>sub</c>, set on every rental from the pool (<see cref="LabDbRegistration"/>). The audit
    /// interceptor reads it from here, so each save records its own caller.
    /// </summary>
    public string? CallerId { get; set; }

    public DbSet<LabResult> Results => Set<LabResult>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfiguration(new LabResultConfiguration());
}
