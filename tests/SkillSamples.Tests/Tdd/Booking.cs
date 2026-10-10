namespace SkillSamples.Tdd;

public sealed class DomainException(string message) : Exception(message);

public sealed record Slot(int Id, DateTimeOffset StartsAt);

public abstract record BookingEvent;
public sealed record BookingCreated(int PatientId, int SlotId) : BookingEvent;
public sealed record BookingCancelled(int SlotId, string Reason) : BookingEvent;

public enum BookingStatus { Booked, Cancelled }

public sealed class Booking
{
    public static readonly TimeSpan CancellationCutoff = TimeSpan.FromHours(2);
    private readonly List<BookingEvent> _events = [];

    private Booking(int patientId, Slot slot) => (PatientId, Slot) = (patientId, slot);

    public int PatientId { get; }
    public Slot Slot { get; }
    public BookingStatus Status { get; private set; } = BookingStatus.Booked;
    public IReadOnlyList<BookingEvent> Events => _events;

    public static Booking Create(int patientId, Slot slot, TimeProvider time)
    {
        if (slot.StartsAt <= time.GetUtcNow())
            throw new DomainException($"Slot {slot.Id} has already started.");

        var booking = new Booking(patientId, slot);
        booking._events.Add(new BookingCreated(patientId, slot.Id));
        return booking;
    }

    public void Cancel(string reason, TimeProvider time)
    {
        if (Status == BookingStatus.Cancelled)
            throw new DomainException("The booking is already cancelled.");
        if (Slot.StartsAt - time.GetUtcNow() < CancellationCutoff)
            throw new DomainException("A booking can't be cancelled less than two hours before it starts.");

        Status = BookingStatus.Cancelled;
        _events.Add(new BookingCancelled(Slot.Id, reason));
    }
}
