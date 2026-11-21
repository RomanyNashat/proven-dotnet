# Visit desk: report

## What I built

`Book`, `Get`, `Cancel` and `ForPatient` in `Clinic/VisitDesk/Desk.cs`. No public type or signature changed.

- **Who may act for a patient:** the caller (`callerId`, from the token) or a dependent they are the guardian of (`IVisitStore.DependentsOf`). All four methods use this one rule. Anyone else gets `NotYourPatient`, `null`, `false` or an empty list. `Get` returns `null` for someone else's visit, the same answer as for an id that doesn't exist.
- **Book** checks these in order: `NotYourPatient`, `NoteTooLong` (over 500 characters; a null note is stored as `""`), `SlotNotFound`, `UnknownPromo`, `SlotInPast` (the slot starts now or earlier), `SlotTaken`, `PromoUsed`. `NotYourPatient` is checked first so a refusal tells you nothing about someone else's patient.
- **Price:** the visit costs the slot's `Price`, less the promo's percent. The `ShownPrice` the app sends is never used, so a client can't set its own price.
- **Promo once per patient:** counted per `PatientId`, case-insensitive. A guardian can't reuse a dependent's code by booking for them.
- **Cancel:** the patient or their guardian can cancel a booked visit while now < start. The visit becomes `Cancelled` and its slot shows in `FreeSlots` again, so it can be booked again.
- **Many users at once:** the store has no atomic check-and-add, so `Book` and `Cancel` do their checks and their write under one lock. Eight bookings of the same slot at once give one visit. Eight bookings at once with the same promo give one discount.

## Tests

`dotnet test Clinic/VisitDesk.Tests`: **125 passed, 0 failed**. I ran this myself after the build.

- `story.md` and `BookingStoryTests.cs`: 13 criteria and 37 edge cases. The criteria are inferred from the request because there was no written story.
- `abuse.md` and `AbuseTests.cs`: 14 abuse cases. They cover a forged price, booking for, reading or cancelling another person's visit, promo reuse (one at a time and at the same moment), double booking, past slots, oversized notes, and cancelling twice.
- The two at-once tests use a slow test store to widen the race window. I wrote all the tests before the code. Before the code, 124 of them failed with `NotImplementedException`; the one that passed is the existing `FreeSlots` test.

## Limitation to know about

The lock only works inside one process. It protects one `Desk` on one store. If the API runs on several instances, or creates a new `Desk` per request (scoped or transient DI), two bookings of one slot, or two uses of one code, can still both succeed. A real store needs this enforced in the database. For example: a unique index on the slot for booked visits, a unique index on (patient, promo code), or a transaction that locks the rows. Please check how the API registers `Desk`. Until the store is fixed it must be a singleton.

## Unclear in the request

1. **"The free slots today":** `FreeSlots` lists every future free slot, not just today's. I left it as it is.
2. **"A guardian sees their dependents' visits":** I built this in `ForPatient` and `Get`. `Mine` still returns only the caller's own visits. Should `Mine` include dependents too?
3. **Who may cancel:** the request says until when, not who. I allowed the patient and their guardian. A dependent can also cancel a visit their guardian booked for them.
4. **Promo rounding:** there's none. 99.99 at 15% is charged as 84.9915. Should it round to 2 places, and which way?
5. **Promo on a cancelled visit:** it still counts as used. I chose this because it's the safer option for money. Should cancelling give the code back?
6. **"Once per patient":** I counted it per patient (the person the visit is for), not per person booking. A guardian can use a code once for themselves and once for each dependent.
7. **Exactly at the start time:** a slot that starts right now can't be booked or cancelled. This matches `FreeSlots`, which treats a slot as free only before it starts.
8. **Empty promo code:** only `null` means no code. `""` is looked up and comes back `UnknownPromo`. Should the app or the API send `null` instead?
9. **Promo percent outside 0–100:** this isn't checked. It's clinic data, but a bad row would give a negative price or a surcharge.
10. **Note length:** counted in UTF-16 units (`string.Length`). An emoji counts as 2.
11. **`ShownPrice` differs from the real price:** the patient is charged the real price without a word. There is no refusal value for "price changed", so the app's receipt could show a different price. Should this refuse the booking instead?
12. **Not your patient:** `ForPatient` returns an empty list and `Get` returns `null`. The API decides whether that becomes a 404 or a 403.
13. **No limit on bookings:** one patient can book every free slot.

## Leftover

`Clinic/VisitDesk.Tests/TestResults/abuse.trx` is a test-run file the adversary agent couldn't delete. It's safe to remove.
