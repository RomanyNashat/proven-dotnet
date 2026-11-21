# Abuse cases: VisitDesk booking

Who may do what: `callerId` (from the token) may book, see and cancel visits whose patient is the caller or
one of `store.DependentsOf(callerId)`. Nobody else, and not the other way round (a dependent is not their
guardian's guardian). Score = likelihood x harm (test-design section 2); 4 or more has a test in
`Clinic/VisitDesk.Tests/AbuseTests.cs`.

- AB1: forger: the app sends a made-up ShownPrice (0, 1, negative, huge); the visit is charged the slot's price, less the promo's percent, whatever was sent (3x3=9)
- AB2: thief: a caller books for someone who is neither themself nor their dependent (another family's child, a stranger, their own guardian, a made-up id); NotYourPatient, no visit, slot stays free (3x3=9)
- AB3: thief: a caller reads someone else's visit by changing the id (stranger, other guardian, other child, the guardian's own dependent); null, the same as an id that doesn't exist (3x3=9)
- AB4: thief: a caller lists the visits of someone who is neither themself nor their dependent; an empty list, nothing about the patient (3x3=9)
- AB5: vandal: a caller cancels someone else's visit by its id; false, the visit stays Booked and its slot stays taken (3x3=9)
- AB6: cheater: a patient uses the same promo code a second time, also re-spelled in another case or by their guardian for them; PromoUsed, nothing stored (3x3=9)
- AB7: cheater: a patient fires the same promo code at many slots at once; exactly one visit gets the discount, the rest are refused PromoUsed (2x3=6)
- AB8: cheater: many patients book the same slot at once; exactly one visit is Booked, the rest are refused SlotTaken (3x2=6)
- AB9: cheater: a caller books a slot that already has a Booked visit; SlotTaken, the first visit untouched (3x2=6)
- AB10: cheater: a patient cancels after the visit started (a no-show passed off as a cancel, a slot freed after the fact); false, it stays Booked (3x2=6)
- AB11: thief: booking for another family's child with that child's used promo, a taken slot, a bad code or a long note; NotYourPatient first, so the refusal says nothing about the child (2x3=6)
- AB12: cheater: a caller books a slot that has started (or starts this instant, which FreeSlots already hides); SlotInPast, nothing stored (2x2=4)
- AB13: vandal: a note of 501 characters or a megabyte; NoteTooLong, nothing stored, slot stays free (2x2=4)
- AB14: cheater: a visit cancelled twice, or an old cancelled visit cancelled again after someone else rebooked the slot; false, the new visit untouched (2x2=4)
