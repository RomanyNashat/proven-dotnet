namespace SkillSamples.EfOutbox;

public interface IDomainEvent;

public abstract class AggregateRoot
{
    private readonly List<Func<IDomainEvent>> _events = [];

    public abstract string AggregateKey { get; }

    public bool HasDomainEvents => _events.Count > 0;

    // Events are built when SaveChanges collects them, not when they're raised, so they can carry an id
    // assigned in between: HiLo gives the aggregate its id when it's added to the context.
    public IReadOnlyList<IDomainEvent> CollectDomainEvents() => _events.Select(create => create()).ToList();

    public void ClearDomainEvents() => _events.Clear();

    protected void Raise(Func<IDomainEvent> domainEvent) => _events.Add(domainEvent);
}

public sealed record AppointmentBooked(int AppointmentId, int ClinicId, DateTimeOffset StartsAt) : IDomainEvent;

public sealed record AppointmentCancelled(int AppointmentId) : IDomainEvent;

public sealed class Appointment : AggregateRoot
{
    private Appointment()
    {
    }

    public int Id { get; private set; }
    public int ClinicId { get; private set; }
    public DateTimeOffset StartsAt { get; private set; }
    public string? Notes { get; private set; }
    public bool IsCancelled { get; private set; }

    public override string AggregateKey => $"appointment-{Id}";

    public static Appointment Book(int clinicId, DateTimeOffset startsAt, string? notes)
    {
        var appointment = new Appointment { ClinicId = clinicId, StartsAt = startsAt, Notes = notes };
        appointment.Raise(() => new AppointmentBooked(appointment.Id, appointment.ClinicId, appointment.StartsAt));
        return appointment;
    }

    public void Cancel()
    {
        if (IsCancelled)
        {
            return;
        }

        IsCancelled = true;
        Raise(() => new AppointmentCancelled(Id));
    }
}
