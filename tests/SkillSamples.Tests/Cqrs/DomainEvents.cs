using Microsoft.Extensions.DependencyInjection;

namespace SkillSamples.Cqrs;

public interface IDomainEvent;

public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken ct);
}

/// <summary>Calls every handler registered for an event. Run after SaveChanges (see ddd-patterns).</summary>
public sealed class DomainEventDispatcher(IServiceProvider services)
{
    public async Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken ct)
    {
        foreach (var domainEvent in events)
        {
            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
            foreach (var handler in services.GetServices(handlerType))
            {
                await (Task)handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!
                    .Invoke(handler, [domainEvent, ct])!;
            }
        }
    }
}
