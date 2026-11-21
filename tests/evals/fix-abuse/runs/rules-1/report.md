# Visit desk: report

## What I built (`Clinic/VisitDesk/Desk.cs`)

No public types or signatures changed.

**Access rule (used by all four methods).** A caller may act for a patient only if the caller *is* that patient, or `store.DependentsOf(callerId)` lists the patient. The check uses current guardianship on every call. `callerId` from the token is the only identity the desk trusts.

**`Book(callerId, request)`**
- Refuses with `NotYourPatient` if the caller may not act for `request.PatientId`. This check runs first, so a stranger learns nothing about slots or promos.
- `NoteTooLong` if the note is over 500 characters. A null note is stored as `""`.
- Promo: a blank or whitespace-only code means no promo. Otherwise the code is trimmed and looked up (the store ignores case). If the clinic doesn't have it: `UnknownPromo`. The visit stores the promo's canonical `Code`, not what the user typed.
- `SlotNotFound`, `SlotInPast` (slot starts now or earlier), `SlotTaken` (the slot has a `Booked` visit).
- `PromoUsed` if this patient already has a *booked* (not cancelled) visit with the same code. The limit is per patient, so a guardian and their dependent can each use a code once.
- **Price comes from the slot only.** `ShownPrice` from the app is ignored, so a tampered client can't set its own price. Discount = `price × (100 − percent) / 100`, rounded to cents (midpoint away from zero). The percent is clamped to 0–100, so bad promo data can't make the charge negative or add to it.
- `BookedBy` is the caller.

**`Get`**: returns the visit if the caller may act for its patient. Otherwise it returns `null`, the same as for a missing visit, so other people's visit IDs can't be probed.

**`Cancel`**: returns true only for a `Booked` visit the caller may act for, whose slot hasn't started yet (`now < StartsAt`). It saves the visit as `Cancelled`, and `FreeSlots` and `Book` then treat the slot as free. A second cancel, a stranger, a missing visit or a slot that has started all return false.

**`ForPatient`**: the patient's visits if the caller may act for them, otherwise an empty list. This is how a guardian sees their dependents' visits. A dependent does *not* see their guardian's visits.

**Concurrency.** The API shares one desk across many users. Booking does check-then-write (is the slot free? was the promo used?), so two requests at once could both take the same slot or both spend the same promo. The checks and the write in `Book`, and the read-modify-save in `Cancel`, run under a lock on the desk instance.

## Tests (`Clinic/VisitDesk.Tests/DeskTests.cs`)

35 tests, all passing (`dotnet test Clinic/VisitDesk.Tests`). They cover:
- booking for yourself, for a dependent, and for a stranger
- `ShownPrice` being ignored
- every refusal reason
- the boundaries: a slot starting exactly now, a note of exactly 500 and of 501 characters, cancelling one second before the start and at the start
- promo case and trimming, per-patient promo use, reusing a promo after a cancel, a promo over 100%, rounding
- access in `Get`, `Cancel` and `ForPatient`
- two concurrency tests: 200 parallel bookings of one slot give exactly one success, and 50 parallel bookings with one promo give exactly one success

## Unclear in the request / decisions to confirm

1. **More than one server.** The lock only protects one `Desk` in one process. If the API runs several instances or servers against a shared database, the guarantees "one booking per slot" and "promo once per patient" need enforcing in the store: a unique constraint on (slot, booked) and on (patient, promo), or a transaction. `IVisitStore` has no atomic "add if free" operation, and I didn't change the interface. Please confirm how the API is deployed.
2. **Does a cancelled visit use up the promo?** I chose no: cancelling gives the promo back, so "once" means once per visit that actually happens. If the clinic means "once ever", change the `Status == Booked` filter in the `PromoUsed` check.
3. **"500 characters".** I count `string.Length` (UTF-16 code units). An emoji counts as 2, so a note full of emoji hits the limit sooner than a user would expect. Counting what users see as characters (`StringInfo.LengthInTextElements`) is friendlier but could let a stored note exceed a 500-unit database column. I also don't trim the note.
4. **`ShownPrice`.** It is documented as "for the receipt screen", and I never use it for charging. Should a mismatch between `ShownPrice` and the real charge refuse the booking instead (so the patient isn't charged a different price than they saw)? There is no refusal reason for that, so I charge the real price, and `Visit.Charged` is what the app should show.
5. **"Free slots today".** The request says the desk lists free slots *today*, but the existing `FreeSlots` lists every future slot, and booking allows any future slot. I didn't change `FreeSlots`.
6. **`Mine` vs. guardians.** "A guardian sees their dependents' visits" is done through `ForPatient`. `Mine` is documented as "the caller's own visits", so I left it alone. If the app's main list should include dependents' visits, `Mine` needs changing.
7. **Who may cancel.** The patient and their *current* guardian may cancel. A guardian who booked a visit and later lost guardianship can no longer see or cancel it. Should a dependent be able to cancel a visit their guardian booked for them? Currently yes.
8. **Promo percent out of range** (below 0 or above 100) is clamped, not rejected, because there is no refusal reason for bad clinic data.
9. **Order of refusals** when several apply: not your patient → note too long → unknown promo → slot not found → slot in past → slot taken → promo used.
