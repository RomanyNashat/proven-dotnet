namespace VisitDesk;

public enum VisitStatus { Booked, Cancelled }

public enum BookRefusal { None, NotYourPatient, SlotNotFound, SlotTaken, SlotInPast, NoteTooLong, UnknownPromo, PromoUsed }

/// <summary>A clinic's appointment slot. <see cref="Price"/> is what the clinic charges for it.</summary>
public sealed record Slot(int Id, int ClinicId, DateTimeOffset StartsAt, decimal Price);

/// <summary>A promo code and the percent it takes off a visit's price.</summary>
public sealed record Promo(string Code, int PercentOff);

public sealed record Visit(int Id, int PatientId, int SlotId, int BookedBy, decimal Charged, string? PromoCode,
    string Note, VisitStatus Status);

/// <summary>What the mobile app sends to book a visit.</summary>
/// <param name="PatientId">Who the visit is for: the signed-in user or one of their dependents.</param>
/// <param name="SlotId">The slot picked from the free slots.</param>
/// <param name="ShownPrice">The price the app showed the patient, for the receipt screen.</param>
/// <param name="PromoCode">A promo code the patient typed, if any.</param>
/// <param name="Note">A note for the doctor, if any.</param>
public sealed record BookRequest(int PatientId, int SlotId, decimal ShownPrice, string? PromoCode, string? Note);

public sealed record BookResult(BookRefusal Refusal, Visit? Visit)
{
    public bool Ok => Refusal == BookRefusal.None;
}
