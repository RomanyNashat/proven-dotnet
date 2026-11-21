using System.Security.Claims;
using LabResults.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LabResults.Infrastructure;

/// <summary>Stamps every new result with the clinician (token <c>sub</c>) who recorded it.</summary>
/// <remarks>
/// The user is read from the current request at save time, never cached: the DbContext (and so this interceptor)
/// is pooled and shared across requests.
/// </remarks>
public sealed class AuditInterceptor(IHttpContextAccessor accessor) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        string? userId = null;
        foreach (var entry in context.ChangeTracker.Entries<LabResult>())
        {
            if (entry.State == EntityState.Added)
            {
                userId ??= CurrentUserId();
                entry.Property(r => r.RecordedBy).CurrentValue = userId;
            }
        }
    }

    private string CurrentUserId()
    {
        var user = accessor.HttpContext?.User;
        var id = user?.FindFirstValue("sub") ?? user?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (user?.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(id))
        {
            // A result must be attributable to a clinician; refuse rather than store a placeholder.
            throw new InvalidOperationException("A lab result can only be recorded by an authenticated user with a subject.");
        }

        return id;
    }
}
