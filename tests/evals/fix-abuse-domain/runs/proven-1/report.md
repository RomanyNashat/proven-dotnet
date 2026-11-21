# Visit desk: report

## What I built

The only file I changed is `Clinic/VisitDesk/Desk.cs`. All public types and signatures are the same as before.

- **`Book`**: books a slot for the caller, or for a dependent while the guardian link holds (`Until` is null or later than now). `BookedBy` is the caller. The charge is the slot's price in the store at booking time. `ShownPrice` is never read, so the app can't set the price. A promo takes its percent off, rounded to 2 decimals with midpoints away from zero. A patient can use each code once, matched with case and spaces ignored. A missing note is stored as `""`, and a note over 500 characters is refused.
- **`Get`**: returns a visit to its patient or to a guardian whose link holds. Anyone else gets null, the same answer as for an id that doesn't exist, so a caller can't probe ids.
- **`Cancel`**: works for the patient or a guardian whose link holds, while the visit is booked and its slot hasn't started. It marks the visit `Cancelled`, which frees the slot, and calls `Refund` with the amount charged, not today's price. It refunds once at most.
- **`ForPatient`**: returns the patient's visits to the patient or to a guardian whose link holds. Anyone else gets an empty list.
- **Many users at once**: `IVisitStore` has no atomic "add if free", so the desk takes a lock around check-then-write in `Book` and `Cancel`. Two people can't book the same slot, one patient can't use a promo twice by sending requests in parallel, and a visit can't be refunded twice.

When several refusals apply, the order is: `NotYourPatient`, `NoteTooLong`, `SlotNotFound`, `SlotInPast`, `UnknownPromo`, `SlotTaken`, `PromoUsed`. Ownership comes first so a stranger learns nothing about the slot.

## Tests

`dotnet test Clinic/VisitDesk.Tests`: **101 passed, 0 failed.**

- `StoryTests.cs`: 67 tests for the 17 criteria and 38 edge cases in `story.md`.
- `AbuseTests.cs`: 33 tests for the 15 abuse cases in `abuse.md`: reading or cancelling another person's visit, booking for someone who isn't your dependent, a forged `ShownPrice`, promo reuse, double refunds, parallel bookings and cancels.
- `DeskTests.cs`: the existing `FreeSlots` test.

The lock was checked by taking it out. The three race tests in `AbuseTests.cs` went red on every run without it. The four race tests in `StoryTests.cs` went red only some of the time, or never. They check the right thing but don't create enough overlap to prove the lock is needed. That is worth fixing in the test helper (pause after the read, start the threads with a `Barrier`); I left their files as written.

## Choices I made where the request is silent

1. **Another clinic's promo, or an expired one**: refused as `UnknownPromo`. A promo expires at its `ValidUntil` instant, checked against booking time, not the visit's start.
2. **A promo with `PercentOff` outside 0–100**: refused as `UnknownPromo`, so a visit can't be charged less than 0 or more than its price.
3. **A blank promo field**: treated as no promo.
4. **A cancelled visit's promo**: still counts as used. Cancelling doesn't give the code back.
5. **`ShownPrice` that differs from the slot's price**: the booking goes through at the slot's price. There is no refusal value for "the price changed".
6. **A slot that has since been deleted**: its visit can't be cancelled, because the desk can't check the start time.

## What I found unclear in the request

1. **Price shown vs. price charged.** If the price changes between the app showing it and the booking, should the patient be charged silently or asked again? `BookRefusal` has no value for that.
2. **Which promo codes the clinic "has".** Is a code issued for another clinic, or an expired one, a code the clinic "doesn't have"? Should it refuse the booking, or book at full price?
3. **Discount rounding.** For example, 15% off 333.33 is 283.3305. The direction of rounding is a finance decision.
4. **Promo after cancel.** Does cancelling give the code back?
5. **Who gets the refund.** The request says the patient, but `IVisitStore.Refund` says "whoever paid". When a guardian books for a child, the guardian probably paid. The desk only passes the visit id, so the store decides.
6. **A guardian link that ends before the visit.** For example, a child turns 18 next week and the visit is in two weeks. Today the desk checks the link against now, so the guardian can still book, see and cancel until the birthday.
7. **A former guardian and visits they booked.** Once the link ends, they can no longer see or cancel a visit they booked and paid for. Is that intended?
8. **"Once per patient" with guardians.** A guardian can use one code once for themselves and once for each dependent. Is that intended?
9. **`BookRefusal.NotAllowed`.** Nothing in the request uses it, so I didn't either.
10. **500 characters.** I count .NET string length (UTF-16 units), so an emoji can count as 2.
11. **No limit on how many slots one person can hold.** One user can book all of a clinic's free slots and cancel them just before they start, for a full refund. The clinic may want a cap or a cancellation cut-off.

## Limits of this change

- **The lock only protects one desk in one process.** The `Desk` remarks say the API uses one desk for every user, and that works. If the API registers `Desk` per request, or runs on more than one server, two bookings can still race. The fix then belongs in the store: a unique booked visit per slot, and an update that cancels only if the visit is still booked.
- Tested only against `InMemoryVisitStore`. A real database store hasn't been tested.
