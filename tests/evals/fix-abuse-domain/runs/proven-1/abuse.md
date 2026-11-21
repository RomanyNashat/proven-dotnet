# Abuse cases: visit desk (Book, Get, Cancel, ForPatient)

Who may do what: `callerId` (from the token) may book, read and cancel for themselves, and book and read for
a dependent whose `GuardianLink` has `GuardianId == callerId` and `Until` null or still ahead. Nobody else.
Score is likelihood x harm (test-design section 2). Tests: `Clinic/VisitDesk.Tests/AbuseTests.cs`.

- AB1: thief: a patient reads another patient's visit by changing the visit id in `Get`; null, the same answer as for an id that doesn't exist (score 9)
- AB2: thief: a patient lists someone else's visits with `ForPatient` (a stranger, or a child asking for its guardian's); empty, the same as for a patient with no visits (score 9)
- AB3: thief: a former guardian (link `Until` already passed) reads or cancels a visit the former dependent booked themselves; `Get` null, `ForPatient` empty, `Cancel` false with no refund (score 6)
- AB4: vandal: a patient cancels another patient's visit by its id; false, the visit stays booked, no refund is paid (score 9)
- AB5: forger: a patient books for someone who isn't their dependent (a stranger, their own guardian, a former dependent); `NotYourPatient`, no visit is added (score 9)
- AB6: forger: the app sends a `ShownPrice` lower than the slot's price (or zero, or negative); the visit is charged the slot's price, whatever was sent (score 9)
- AB7: forger: a patient uses a promo code issued for another clinic, or one past its `ValidUntil`; the booking is refused and no visit is added (score 6)
- AB8: cheater: a patient uses the same promo a second time, also retyped in another case or with spaces; `PromoUsed`, no second discounted visit (score 9)
- AB9: cheater: one patient fires many bookings at once, each on a different slot with the same promo; at most one visit carries the promo (score 9)
- AB10: cheater: a patient cancels the same visit twice, or after it has started; one refund in total, and none after the start (score 9)
- AB11: cheater: a patient books with a promo, the slot's price then goes up, and they cancel; the refund is what was charged, not the slot's current price (score 6)
- AB12: cheater: many patients book the same slot at the same moment (or one after another); exactly one visit is booked on it, the rest get `SlotTaken` (score 9)
- AB13: cheater: a patient fires many cancels of one visit at once; exactly one returns true and exactly one refund is paid (score 9)
- AB14: cheater: a patient books a slot that has already started (by its id, it isn't in the free list); `SlotInPast`, no visit (score 4)
- AB15: vandal: a patient sends a note of 501 characters or a megabyte; `NoteTooLong`, no visit, nothing stored (score 4)
