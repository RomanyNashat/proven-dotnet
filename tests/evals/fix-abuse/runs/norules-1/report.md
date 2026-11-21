# Visit desk: report

## What I built (`Clinic/VisitDesk/Desk.cs`)

I didn't change any public types or signatures.

**Who may act for a patient.** A caller may book, read and cancel visits for a patient only if the caller *is* that patient or the store lists the caller as that patient's guardian (`DependentsOf(callerId)`). This rule covers all four methods. `callerId` is the only identity the desk trusts.

**`Book(callerId, request)`**, checks in this order:
1. The caller may not act for the patient → `NotYourPatient`.
2. The note is over 500 characters → `NoteTooLong`. A null note is stored as `""`.
3. The slot doesn't exist → `SlotNotFound`.
4. The promo code is non-blank and not in the store → `UnknownPromo`. A blank or whitespace code means "no promo". Codes are trimmed and matched without regard to case, as the store does. The visit records the code as the store spells it.
5. Under a lock:
   - The slot has already started (`StartsAt <= now`) → `SlotInPast`.
   - The slot has a booked visit → `SlotTaken`.
   - The patient already has a booked visit with this promo → `PromoUsed`.
   - Otherwise it creates the visit with `BookedBy = callerId`.
- **The charge always comes from the slot's price in the store.** `BookRequest.ShownPrice` comes from the app and could be tampered with, so the desk never uses it for money.
- The discount is `price × (100 − percent) / 100`, rounded to cents (midpoint away from zero). The percent is clamped to 0–100.

**`Get(callerId, visitId)`** returns the visit, or `null` when the visit doesn't exist or the caller may not see it. The two cases look the same, so the API can't be used to find out which visit IDs exist.

**`Cancel(callerId, visitId)`** returns `true` and sets the visit to `Cancelled` only if:
- the visit exists,
- it is still `Booked`,
- the caller may act for its patient,
- and its slot hasn't started.

Otherwise it returns `false`. A cancelled visit no longer counts as booked, so its slot shows up in `FreeSlots` and can be booked again.

**`ForPatient(callerId, patientId)`** returns the patient's visits if the caller is that patient or their guardian. For anyone else it returns an empty list.

**Concurrency.** `IVisitStore` has no atomic "book if free" operation, so booking checks the slot and then adds the visit in two steps. Two simultaneous requests could both pass the check and double-book a slot, or use one promo twice. To prevent that:
- Booking's check-and-add and cancelling run under one lock per store instance.
- The lock is shared by every `Desk` on that store, so it still works if the API creates a desk per request.
- Everything that doesn't write runs outside the lock.

**Limit:** this only works within one process. If the API runs on several servers against a shared database, the store needs to enforce it: a unique "booked visit per slot" constraint, or a conditional insert.

## Tests (`Clinic/VisitDesk.Tests/DeskTests.cs`)

38 tests, all passing (`dotnet test Clinic/VisitDesk.Tests`). They cover:
- booking for yourself, for a dependent, and for someone you're not allowed to book for (refused);
- a slot that doesn't exist, has started, starts exactly now, or is taken;
- `ShownPrice` being ignored;
- the promo discount, rounding, matching regardless of case or spaces, blank codes, unknown codes, reuse, a promo being per patient, and reuse after a cancellation;
- notes at and over the limit;
- `Get`, `Cancel` and `ForPatient`, each for the patient, their guardian and an unrelated user;
- cancelling after the visit starts, cancelling twice, and a stale cancel after the slot was rebooked;
- two concurrency tests: 50 users racing for one slot, and 30 parallel bookings with the same promo.

I didn't confirm that the concurrency tests fail when the lock is removed. That check needed an in-place edit I didn't get approval for. The tests pass with the lock, but I haven't shown they would catch a race without it.

## What was unclear (and what I chose)

1. **`ShownPrice`.** The model says it is "for the receipt screen", but it's an input from the app. I charge the slot's stored price and ignore `ShownPrice` completely. If the clinic wants a booking refused when the shown price doesn't match (for example, the price changed after the app loaded it), there's no `BookRefusal` value for that. It would need a new enum member.
2. **Is a promo "used" if the visit is cancelled?** I treat a cancelled visit as not using the promo, so the patient can use it again. If the clinic means "once ever", change the `PromoUsed` check so it also counts cancelled visits.
3. **"Once per patient" vs once per account.** I count it per *patient*. A guardian can use the same code once for themselves and once for each dependent. If it's meant to be once per paying account (`BookedBy`), the check needs to change.
4. **`Mine` and guardians.** "A guardian sees their dependents' visits as well as their own" could mean `Mine` should include dependents' visits. `Mine` was already built and documented as "the caller's own visits", so I left it alone. Guardians see dependents through `ForPatient` (and `Get`). If the app's "my visits" screen should show the whole family, `Mine` needs to change.
5. **Who may cancel.** The patient or their current guardian may cancel. The person who booked the visit (`BookedBy`) gets no special right: if a guardianship is later removed, that guardian can no longer see or cancel the visit. A dependent can see and cancel visits their guardian booked for them.
6. **Can dependents book for themselves?** Yes, by the same rule as anyone booking for themselves. The request didn't say whether dependents (e.g. children) have their own sign-in or should be limited.
7. **"500 characters".** I count .NET `string.Length` (UTF-16 code units), so some emoji count as 2. This probably matches a database column limit. If the clinic means characters as a user sees them, count text elements instead.
8. **"Until it starts".** A visit can be cancelled while `now < StartsAt`, and booking needs the same. A slot starting exactly now can't be booked or cancelled. There's no cut-off window (e.g. 24 hours' notice) and no refund handling. The request mentioned neither.
9. **Unauthorized vs not found.** `Get` returns `null` and `Cancel` returns `false` for both, on purpose. `ForPatient` returns an empty list for an unauthorized caller rather than throwing, so the API can't tell "no visits" from "not allowed". If the API should return 403, the signatures would need to carry that.
10. **"Free slots today".** The description says the desk lists free slots *today*, but the existing `FreeSlots` returns every future free slot. I left it unchanged because it wasn't one of the methods to build.
11. **Promo data.** The request didn't say how to handle a promo percent outside 0–100. I clamp it. Promo codes have no expiry or validity window in the model, so there's nothing to check there.
