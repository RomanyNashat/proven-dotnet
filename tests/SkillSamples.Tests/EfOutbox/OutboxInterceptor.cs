using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace SkillSamples.EfOutbox;

// Turns the domain events on tracked aggregates into outbox rows inside the same SaveChanges, so they
// commit or fail with the data. Application code raises events and never writes the outbox by hand.
public sealed class OutboxInterceptor(TimeProvider time) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        AddOutboxMessages(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        AddOutboxMessages(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void AddOutboxMessages(DbContext? db)
    {
        if (db is null)
        {
            return;
        }

        var now = time.GetUtcNow();
        var aggregates = db.ChangeTracker.Entries<AggregateRoot>()
            .Select(e => e.Entity)
            .Where(a => a.HasDomainEvents)
            .ToList();   // materialise first: adding rows below changes the tracker
        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.CollectDomainEvents())
            {
                db.Set<OutboxMessage>().Add(OutboxMessage.From(domainEvent, aggregate.AggregateKey, now));
            }

            // Safe even if the save then fails: the rows stay tracked as Added, so a retried
            // SaveChanges (an execution strategy) still inserts them.
            aggregate.ClearDomainEvents();
        }
    }
}
