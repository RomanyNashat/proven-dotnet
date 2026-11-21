# Story: book, see and cancel clinic visits (VisitDesk)

No formal story. The criteria come from the clinic's request (task.md), the model's own documented
rules (Model.cs, VisitStore.cs) and what `Desk.FreeSlots` already enforces. Every line is `(inferred)`
until the clinic confirms it. Tests: `Clinic/VisitDesk.Tests/StoryTests.cs`.

## Acceptance criteria

- AC1: (inferred) A patient books a free slot for themselves; the visit is for them, booked by them, and the booking returns Ok with the visit.
- AC2: (inferred) A patient books for a dependent they are the guardian of, while the registry's link holds (no `Until`, or `Until` still ahead); the visit is for the dependent and `BookedBy` is the guardian.
- AC3: (inferred) Booking for anyone who is not the caller and not a dependent with a link that holds is refused with `NotYourPatient`, and nothing is booked.
- AC4: (inferred) The visit costs the slot's price as the store has it when booking; the price the app sent (`ShownPrice`) never sets what is charged.
- AC5: (inferred) A slot can be booked only when it exists (`SlotNotFound`), has not started (`SlotInPast`, same rule as `FreeSlots`: it must start after now) and has no booked visit (`SlotTaken`); a cancelled visit does not hold its slot.
- AC6: (inferred) A promo code takes its percent off the slot's price; the visit records the code.
- AC7: (inferred) A code the clinic doesn't have refuses the booking with `UnknownPromo`, and nothing is booked.
- AC8: (inferred) A promo code is used once per patient (the visit's patient, not the caller); a second booking with it for the same patient is refused with `PromoUsed`.
- AC9: (inferred) A promo issued for another clinic, or whose `ValidUntil` has passed, does not take anything off and the booking is refused (the refusal code is open, Q2).
- AC10: (inferred) The note for the doctor is optional and up to `MaxNoteLength` (500) characters; a longer one is refused with `NoteTooLong`. No note is stored as an empty note.
- AC11: (inferred) Two bookings at once for the same slot (the API serves many users on one desk) book it once: one Ok, the other `SlotTaken`.
- AC12: (inferred) A refused booking changes nothing: no visit, the slot stays free, a promo it carried stays unused.
- AC13: (inferred) `Get` returns a visit to its patient and to the patient's guardian while the link holds; to anyone else, and for an id that doesn't exist, it returns null.
- AC14: (inferred) A booked visit can be cancelled until it starts, by its patient or their guardian while the link holds; cancelling sets it to `Cancelled`, returns true and frees the slot.
- AC15: (inferred) Cancelling refunds the patient, once, the amount that was charged (`IVisitStore.Refund(visitId, Charged)`), whatever the slot costs now.
- AC16: (inferred) Cancel returns false and refunds nothing when the visit has started, is already cancelled, doesn't exist, or the caller is not its patient nor their guardian.
- AC17: (inferred) `ForPatient` returns a patient's visits to the patient and to their guardian while the link holds, the same list `Mine` gives the patient; to anyone else it returns an empty list.

## Edge cases

- EC1: AC2, state (registry): the link ended a tick before now, so the guardian is refused `NotYourPatient`
- EC2: AC2, state (registry): the link has an `Until` after the visit, so the guardian may book
- EC3: AC2, every pair: the registry has an ended link and a newer open one for the same pair; the open one holds
- EC4: AC3, ownership: a dependent books for their guardian (the link read backwards); refused `NotYourPatient`
- EC5: AC3, ownership: a guardian books for another guardian's dependent; refused `NotYourPatient`
- EC6: AC4, forger: `ShownPrice` lower than the slot's price; charged the slot's price
- EC7: AC4, follow the data: the slot's price changes after it was shown; charged the new price
- EC8: AC5, boundary: the slot starts exactly now; refused `SlotInPast`
- EC9: AC5, boundary: the slot starts one tick after now; booked
- EC10: AC5, state: the slot's visit was cancelled; it can be booked again
- EC11: AC5, broken rules: a slot id that doesn't exist; refused `SlotNotFound`
- EC12: AC6, decision table: the code typed in another case and with spaces is the same code
- EC13: AC8, decision table: the same code for two different dependents of one guardian is used once by each
- EC14: AC8, same thing twice: the code typed in another case the second time is still `PromoUsed`
- EC15: AC8, two at once: the same patient books two slots at once with the same code; only one gets it
- EC16: AC9, decision table: a promo for another clinic is refused; one with no clinic works at any clinic
- EC17: AC9, boundary: a promo whose `ValidUntil` passed a day ago is refused; one valid until tomorrow works
- EC18: AC10, boundary: a note of exactly 500 characters is kept
- EC19: AC10, boundary: a note of 501 characters is refused `NoteTooLong`
- EC20: AC10, Arabic text: 500 Arabic letters are kept, 501 refused
- EC21: AC10, none: no note is stored as an empty string
- EC22: AC12, refused booking: refused for its note, the promo it carried is still usable after
- EC23: AC13, ownership: another user's visit id returns null
- EC24: AC13, state (registry): the guardian's link ended; the dependent's visit returns null
- EC25: AC14, boundary: cancelled one tick before the visit starts; allowed
- EC26: AC14, boundary: cancelled exactly when the visit starts; refused, no refund
- EC27: AC14, follow the data: after cancelling, the slot is in `FreeSlots` and another patient can book it
- EC28: AC15, follow the data: the slot's price rose after booking; refund is what was charged
- EC29: AC15, follow the data: a visit booked with a promo refunds the discounted amount
- EC30: AC16, same thing twice: cancelling twice refunds once; the second call returns false
- EC31: AC16, two at once: two cancels at once refund once
- EC32: AC16, ownership: another user cancels the visit; false, nothing changes
- EC33: AC16, broken rules: a visit id that doesn't exist returns false
- EC34: AC17, ownership: a stranger asks for a patient's visits; empty
- EC35: AC17, state (registry): the guardian's link ended; empty
- EC36: AC17, follow the data: a cancelled visit is still in the list, with its status, as in `Mine`
- EC37: AC3, decision table: not the caller's patient and the slot taken; refused `NotYourPatient` first, saying nothing about the slot
- EC38: AC11, two at once: many users race for one slot; exactly one visit is booked

## Open questions (for the clinic)

- Q1: `ShownPrice` and the slot's price differ (the price changed after the app showed it). Charge the slot's price silently (what the tests assume, AC4), or refuse so the patient sees the new price first? `BookRefusal` has no "price changed" value.
- Q2: A promo for another clinic, or expired: which refusal? `UnknownPromo`, `NotAllowed`, or book it at full price? Tests assert only that it is refused and nothing is booked.
- Q3: `ValidUntil`: is the promo valid at that exact instant, and is it checked against the booking time or the visit's start?
- Q4: Promo rounding: 15% off 333.33 is 283.3305. Round to what (2 decimals, whole halalas?), and which way?
- Q5: Does a cancelled visit give its promo back to the patient, or is the code spent?
- Q6: Is an empty or blank `PromoCode` ("" from the app's empty field) no promo, or `UnknownPromo`?
- Q7: A guardian link whose `Until` falls between now and the visit (a child turning 18 next week): may the guardian book, see or cancel it? Tests avoid that window.
- Q8: May a guardian whose link has since ended still cancel a visit they booked and paid for? And who gets the refund: `Refund` says "whoever paid", but the request says "refunds the patient".
- Q9: The note's 500 characters: UTF-16 units (`string.Length`) or what the user sees (an emoji is 2 units, some 4+)? Arabic letters agree either way, so the tests use them.
- Q10: What is `BookRefusal.NotAllowed` for? Nothing in the request maps to it.
- Q11: A 100% promo makes a visit free: is `Refund(visitId, 0)` still called on cancel? And a promo with `PercentOff` outside 0..100 from the marketing tool?
- Q12: May a patient book two visits at the same time at different slots, or the same doctor twice a day? Nothing limits it now.
- Q13: When several refusals apply (not your patient, unknown promo, note too long), which wins? Tests fix only that ownership is checked first (EC37), so a stranger learns nothing about the slot.
- Q14: Does `Get`/`ForPatient` for a visit of the caller's dependent include visits booked by someone else (the other parent)? Tests assume yes: the guardian sees all the dependent's visits.
