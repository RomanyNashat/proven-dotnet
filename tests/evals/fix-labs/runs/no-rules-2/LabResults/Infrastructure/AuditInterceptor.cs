using System.Security.Claims;
using LabResults.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LabResults.Infrastructure;

/// <summary>Stamps every new result with the clinician who recorded it.</summary>
/// <remarks>
/// Registered as a singleton: pooled DbContext options (and their interceptors) are built once,
/// so the user must be read at save time rather than captured at construction.
/// </remarks>
public sealed class AuditInterceptor(IHttpContextAccessor accessor) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context!);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context!);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext context)
    {
        foreach (var entry in context.ChangeTracker.Entries<LabResult>())
        {
            if (entry.State == EntityState.Added)
            {
                var user = accessor.HttpContext?.User;
                var id = user?.FindFirstValue("sub") ?? user?.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? throw new InvalidOperationException("Cannot record a result without an authenticated user.");
                entry.Property(r => r.RecordedBy).CurrentValue = id;
            }
        }
    }
}
