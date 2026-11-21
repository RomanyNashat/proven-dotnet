namespace VisitDesk;

public enum VisitStatus { Booked, Cancelled }

public enum BookRefusal { None, NotYourPatient, SlotNotFound, SlotTaken, SlotInPast, NoteTooLong, UnknownPromo, PromoUsed, NotAllowed }

/// <summary>A clinic's appointment slot. <see cref="Price"/> is what the clinic charges for it today.</summary>
public sealed record Slot(int Id, int ClinicId, DateTimeOffset StartsAt, decimal Price);

/// <summary>A promo code from the marketing team's tool.</summary>
/// <param name="ClinicId">The clinic it was issued for; null for every clinic.</param>
/// <param name="ValidUntil">Null when it doesn't end.</param>
public sealed record Promo(string Code, int PercentOff, int? ClinicId, DateTimeOffset? ValidUntil);

/// <summary>A guardian's link to a dependent, as the family registry keeps it.</summary>
/// <param name="Until">When the link ended or ends (a child turning 18, a court order); null while it holds.</param>
public sealed record GuardianLink(int GuardianId, int DependentId, DateTimeOffset? Until);

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
