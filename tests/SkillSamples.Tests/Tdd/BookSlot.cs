using FluentValidation;

namespace SkillSamples.Tdd;

public sealed record BookSlot(int PatientId, int SlotId);

public enum BookSlotOutcome { Booked, SlotNotFound, SlotTaken }

public interface ISlotRepository
{
    Task<Slot?> GetAsync(int slotId, CancellationToken ct);
}

public interface IBookingRepository
{
    Task<bool> IsTakenAsync(int slotId, CancellationToken ct);
    void Add(Booking booking);
}

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct);
}

public sealed class BookSlotHandler(
    ISlotRepository slots, IBookingRepository bookings, IUnitOfWork unitOfWork, TimeProvider time)
{
    public async Task<BookSlotOutcome> Handle(BookSlot command, CancellationToken ct)
    {
        var slot = await slots.GetAsync(command.SlotId, ct);
        if (slot is null)
            return BookSlotOutcome.SlotNotFound;

        // A friendly answer for the common case; the unique index on the slot is what stops two at once.
        if (await bookings.IsTakenAsync(slot.Id, ct))
            return BookSlotOutcome.SlotTaken;

        bookings.Add(Booking.Create(command.PatientId, slot, time));
        await unitOfWork.SaveChangesAsync(ct);
        return BookSlotOutcome.Booked;
    }
}

public sealed class BookSlotValidator : AbstractValidator<BookSlot>
{
    public BookSlotValidator()
    {
        RuleFor(c => c.PatientId).GreaterThan(0);
        RuleFor(c => c.SlotId).GreaterThan(0);
    }
}
