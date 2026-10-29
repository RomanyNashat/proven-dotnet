using LabResults.Domain;
using Microsoft.EntityFrameworkCore;

namespace LabResults.Infrastructure;

public sealed class LabDbContext(DbContextOptions<LabDbContext> options) : DbContext(options)
{
    /// <summary>
    /// The caller's <c>sub</c>, set on every rental from the pool (see <see cref="LabDbRegistration"/>).
    /// Not mapped. Null outside a request, so only saves that need a caller fail.
    /// </summary>
    public string? CallerId { get; set; }

    public DbSet<LabResult> Results => Set<LabResult>();

    public DbSet<SmsOutboxMessage> SmsOutbox => Set<SmsOutboxMessage>();

    public DbSet<AccessAuditEntry> AccessAudit => Set<AccessAuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LabDbContext).Assembly);
}
