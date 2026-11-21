# Visit desk: report

## What I built

All four methods in `Clinic/VisitDesk/Desk.cs`. No public types or signatures changed.

**Who may act for a patient.** The rule is the same in all four methods. The caller is the patient, or the caller has a `GuardianLink` to them whose `Until` is null or still in the future. Links only go one way: a dependent can't act for their guardian. Ended links grant nothing.

**`Book(callerId, request)`** checks, in this order:
1. The caller may act for `PatientId`. If not: `NotYourPatient`.
2. The note is at most 500 characters (null becomes `""`). If not: `NoteTooLong`.
3. The slot exists (`SlotNotFound`), hasn't started (`SlotInPast`, and a slot starting right now counts as started), and has no booked visit (`SlotTaken`).
4. If a promo code is given (trimmed; blank means no code), it must exist, be for this clinic or for all clinics, and not be past `ValidUntil`. If not: `UnknownPromo`. If the same patient already has a *booked* visit with that code: `PromoUsed`.
5. The charge is the slot's **current** `Price` less the promo's percent, rounded to cents (midpoint away from zero), with percent clamped to 0–100. `ShownPrice` from the app is never used for money. The visit records `BookedBy = callerId` and the promo's canonical code.

**`Get`** returns the visit only if the caller may act for its patient. Otherwise it returns null, so "not found" and "not yours" look the same.

**`Cancel`** works only on a booked visit the caller may act for, whose slot hasn't started. It marks the visit `Cancelled`, which frees the slot (`FreeSlots` already ignores cancelled visits). It then calls `Refund(visit.Id, visit.Charged)`: the refund is what was actually paid, not the slot's price today. Any other case returns false.

**`ForPatient`** returns all the patient's visits, cancelled ones included, if the caller may act for them. Otherwise it returns an empty list.

**Many users at once.** Book and cancel both check and then write, and `IVisitStore` has no atomic operation. Without protection, two users could book one slot, one patient could use a promo twice, or one cancel could pay out two refunds. Both methods run under a lock. The lock belongs to the **store instance**, not the desk, so it still works if the API creates a `Desk` per request (scoped/transient DI). Reads take no lock.

**Tests.** `Clinic/VisitDesk.Tests/DeskTests.cs` has 36 tests, and all pass (`dotnet test Clinic/VisitDesk.Tests`). They cover each refusal, guardian and ended-link cases, promo scope, expiry, case and rounding, the refund amount after a price change, double cancel, and parallel tests: 64 callers on one slot, 32 bookings with the same promo, and 32 cancels of one visit, each using a desk per request.

## Unclear points / decisions you may want to revisit

1. **Shown price vs charged price.** "The visit costs the slot's price", and the slot's price can change, so I charge today's price and ignore `ShownPrice`. A patient can therefore be charged more than the app showed. There is no "price changed" refusal in `BookRefusal`. If the clinic wants a booking refused when the price no longer matches, a new enum value is needed, or `NotAllowed` could be reused.
2. **Promo after cancelling.** I count only *booked* visits as using up a code. A cancelled visit is refunded in full, so the patient got nothing from it and may use the code again. If marketing means "once, ever", that's a one-line change.
3. **"Once per patient" means the visit's patient**, not the person booking. A guardian and each dependent can each use the code once. A guardian with five children gets six discounts. Is that intended?
4. **Expired or other-clinic promo → `UnknownPromo`.** "A code the clinic doesn't have" fits a code issued for another clinic. Expiry has no refusal of its own, so I used `UnknownPromo` there too. `NotAllowed` is never returned. I couldn't tell what it's for.
5. **Promo expiry is checked at booking time**, not at the slot's start time.
6. **Rounding.** Not specified. I round to 2 decimals, midpoint away from zero. Check this against what the payments side expects.
7. **"500 characters"** is counted as `string.Length` (UTF-16 code units), which is what a 500-char DB column holds. An emoji counts as 2, so a user near the limit may be refused earlier than the app's counter suggests. The app should count the same way.
8. **Ended guardian links.** Once a link ends (e.g. the child turns 18), the former guardian can no longer see, cancel or list those visits, even ones they booked and paid for. That seemed right for privacy. The refund still goes "to whoever paid" via the store.
9. **Cancel when the slot no longer exists** returns false, because I can't tell whether the visit has started. It might be better to allow it and refund.
10. **The lock only works inside one process.** If the API runs on several servers against a shared database, double booking is still possible. That needs a store-level guarantee, such as a unique index on (slot, booked) or a conditional insert/update, and `IVisitStore` doesn't offer one today. Also, `Save` then `Refund` are two separate calls: if `Refund` throws, the visit is cancelled with no refund made.
11. **"Free slots today"** in the request: the existing `FreeSlots` lists every future free slot, not just today's. I left it unchanged.
12. **Refund amount** is `Charged` (after the discount), not the slot's list price. That is my reading of "refunds the patient".
