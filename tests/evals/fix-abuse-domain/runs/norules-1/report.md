# Visit desk: report

## What I built

All in `Clinic/VisitDesk/Desk.cs`. No public types or signatures changed.

- **Who may act for a patient.** The caller (from their token) may act for themselves, or for a patient they have a guardian link to that **hasn't ended**: `Until` is null or later than now. Links that have ended give no access, even to visits the former guardian booked. `Book`, `Get`, `Cancel` and `ForPatient` all use this one check.
- **`Book`**, checked in this order:
  1. The caller may act for the patient, else `NotYourPatient`. This runs first, so outsiders can't learn anything about slots or promos.
  2. The note is at most 500 characters (null is stored as `""`), else `NoteTooLong`.
  3. The slot exists (`SlotNotFound`), hasn't started (`SlotInPast`) and has no booked visit (`SlotTaken`).
  4. Promo (blank counts as none; trimmed; case doesn't matter). It is refused with `UnknownPromo` if it isn't in the store, was issued for a different clinic, or has passed `ValidUntil`. It is refused with `PromoUsed` if this patient already has a *booked* visit with it. The discount is clamped to 0–100% and rounded to cents (midpoint rounds away from zero).
  5. The charge is the slot's price **at booking time**, less the promo. `BookRequest.ShownPrice` comes from the app and is never used to set the charge.
  6. `BookedBy` is set to the caller.
- **`Get`**: returns the visit if the caller may act for its patient. Otherwise it returns null, the same as for an unknown id, so you can't tell "not yours" apart from "doesn't exist".
- **`Cancel`**: the patient or a current guardian can cancel a visit that is still `Booked`, as long as its slot hasn't started yet (`StartsAt > now`). It marks the visit `Cancelled`, which frees the slot, and calls `Refund(visit.Id, visit.Charged)`. That refunds what was actually paid, not the slot's current price. Cancelling a second time returns false and refunds nothing.
- **`ForPatient`**: the patient's visits if the caller may act for them, otherwise an empty list.
- **Concurrency.** `Book` and `Cancel` check the store and then write to it. One lock inside the desk makes each call atomic, so concurrent requests can't double-book a slot, use a promo twice, or refund the same visit twice. Tests cover all three.

Tests: `Clinic/VisitDesk.Tests/DeskTests.cs`, 37 tests covering every rule above, including expiring guardian links and concurrent callers. All pass with `dotnet test Clinic/VisitDesk.Tests`.

## Unclear or worth deciding

1. **Locking only works inside one process.** If the API runs on more than one instance, or another service writes to the same store, the desk lock doesn't prevent a double booking or a double refund. A real store needs to enforce this itself, for example with a unique booked visit per slot or a conditional update on status. `IVisitStore` has no way to express that today.
2. **Is a promo "used" if its visit was cancelled?** I decided no: cancelling refunds the discounted price, so the patient can use the code again. If marketing means one use, ever, change the check to count cancelled visits too.
3. **Expired promos and promos for other clinics** return `UnknownPromo`, since the clinic doesn't have them today. There's no separate refusal for these, so the app can't tell the user *why*.
4. **`ShownPrice`** is ignored. If the slot price changes between the app showing it and the booking, the patient pays the new price without being told. There is no refusal for "price changed"; adding one would change the public enum.
5. **`BookRefusal.NotAllowed`** is never returned. I used `NotYourPatient` for every access failure, ended guardian links included. I couldn't tell what `NotAllowed` was meant for.
6. **`Mine`** is left unchanged: it still returns only the caller's own visits, as its doc says. "A guardian sees their dependents' visits as well" is met through `ForPatient` and `Get`. If the app expects `Mine` to include dependents' visits, that is a one-line change, but it changes what existing callers get back.
7. **How "500 characters" is counted.** I count UTF-16 code units (`string.Length`), so an emoji counts as 2. If you mean user-visible characters, count text elements instead.
8. **Visibility after guardianship ends.** A guardian whose link has ended loses access to everything, including visits they booked and paid for. That seemed right for cases like court orders, but it is a policy choice.
9. **Who gets the refund.** `Refund` says it pays back "whoever paid". The desk doesn't record a payer apart from `BookedBy`, so who actually receives the money is up to the store.
10. **Promo percentages outside 0–100** come from marketing's tool, so I clamp them rather than trust them. Otherwise a bad value could produce a negative charge.
