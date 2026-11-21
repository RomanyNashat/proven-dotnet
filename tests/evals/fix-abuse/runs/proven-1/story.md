# Story: book, see and cancel a clinic visit

No written story: the criteria below come from the clinic's request (task.md), from what the code already
enforces (Model.cs, VisitStore.cs, Desk.cs) and from the public signatures. Every line is `(inferred)` until
the clinic confirms it. Tests: `Clinic/VisitDesk.Tests/BookingStoryTests.cs`.

The request, verbatim:
- A patient books a free slot for themselves, or for a dependent they are the guardian of. The visit costs the slot's price.
- A promo code takes its percent off the price, once per patient. A code the clinic doesn't have refuses the booking.
- The patient may leave a note for the doctor, up to 500 characters.
- A visit can be cancelled until it starts, and a cancelled visit frees its slot.
- A guardian sees their dependents' visits as well as their own.
- The mobile app calls the desk through the API, for many users at once. `callerId` is the signed-in user, from their token.

## Acceptance criteria

- AC1: (inferred) A patient books a free slot for themselves: the visit is Booked, its PatientId and BookedBy are the caller, for the slot asked for.
- AC2: (inferred) A guardian books a free slot for a dependent they are the guardian of: PatientId is the dependent, BookedBy the guardian. Booking for anyone else is refused with NotYourPatient and books nothing.
- AC3: (inferred) The visit is charged the slot's price; the ShownPrice the app sends does not change what is charged.
- AC4: (inferred) A promo code takes its percent off the slot's price (Charged = Price x (100 - PercentOff) / 100) and the visit keeps the code; with no code, Charged is the price and PromoCode is null.
- AC5: (inferred) A code the clinic doesn't have refuses the booking with UnknownPromo, and nothing is booked.
- AC6: (inferred) A code is used once per patient: a second booking for the same patient with the same code is refused with PromoUsed; another patient may still use it.
- AC7: (inferred) A note for the doctor of up to 500 characters (Desk.MaxNoteLength) is kept as written; a longer note is refused with NoteTooLong; no note is stored as an empty string.
- AC8: (inferred) Only a free slot can be booked: an unknown slot is refused with SlotNotFound, a slot with a booked visit with SlotTaken, a slot that has started (StartsAt <= now, as FreeSlots counts it) with SlotInPast. A refused booking creates no visit.
- AC9: (inferred) A visit can be cancelled by its patient or the patient's guardian until it starts (now < StartsAt): Cancel returns true and the visit is Cancelled. Once started, already cancelled, unknown, or someone else's: Cancel returns false and nothing changes.
- AC10: (inferred) A cancelled visit frees its slot: FreeSlots lists it again and it can be booked again.
- AC11: (inferred) Get returns a visit to its patient and to the patient's guardian, cancelled or not; anyone else, and an unknown id, gets null.
- AC12: (inferred) ForPatient returns a patient's visits (cancelled ones included, as Mine does) to the patient and to their guardian, so a guardian sees their own and their dependents'; anyone else gets an empty list.
- AC13: (inferred) Many users at once: bookings of the same slot at the same moment give exactly one visit, the others are refused with SlotTaken; the same patient's code used at the same moment is used once.

## Edge cases

- EC1: AC2, decision table: a guardian books for another guardian's dependent; NotYourPatient, no visit
- EC2: AC2, decision table: a dependent books for their own guardian (guardianship runs one way); NotYourPatient
- EC3: AC2, decision table: a stranger books for a patient who has no guardian; NotYourPatient
- EC4: AC3, broken rules: the app's ShownPrice is lower than the slot's price; Charged is the slot's price
- EC5: AC4, boundary: a 0 percent code; Charged is the full price
- EC6: AC4, boundary: a 100 percent code; Charged is 0
- EC7: AC4, code-enforced: the code typed in another case matches (the store looks codes up case-insensitively)
- EC8: AC5, follow the data: after an UnknownPromo refusal the slot is still free and the patient has no visit
- EC9: AC6, same thing twice: the same code in another case for the same patient; PromoUsed
- EC10: AC6, ownership: a code one patient used still takes its percent off for another patient
- EC11: AC7, boundary: a note of exactly 500 characters is kept
- EC12: AC7, boundary: a note of 501 characters is refused with NoteTooLong and books nothing
- EC13: AC7, none: no note (null) is stored as an empty string
- EC14: AC7, Arabic text against a length limit: 500 Arabic characters are kept
- EC15: AC8, boundary: a slot starting exactly now; SlotInPast
- EC16: AC8, boundary: a slot starting one tick after now is booked
- EC17: AC8, boundary: a slot that started one tick ago; SlotInPast
- EC18: AC8, state: a slot with a booked visit; SlotTaken, no second visit
- EC19: AC8, broken rules: a slot id the clinic doesn't have; SlotNotFound
- EC20: AC9, boundary: cancelled one tick before it starts; true, Cancelled
- EC21: AC9, boundary: cancelled exactly when it starts; false, still Booked
- EC22: AC9, state transitions: cancelled twice; the second returns false
- EC23: AC9, ownership: a stranger cancels someone's visit; false, still Booked
- EC24: AC9, decision table: a guardian cancels their dependent's visit; true
- EC25: AC9, broken rules: an unknown visit id; false
- EC26: AC10, follow the data: after a cancel, Get, ForPatient and Mine all show it Cancelled
- EC27: AC10, follow the data: another patient books the freed slot; it leaves FreeSlots again
- EC28: AC11, ownership: a stranger asks for someone's visit; null
- EC29: AC11, decision table: a guardian asks for their dependent's visit; the visit
- EC30: AC11, decision table: a dependent asks for their guardian's visit; null
- EC31: AC11, broken rules: an unknown visit id; null
- EC32: AC12, decision table: a guardian asks for their own and for their dependent's visits; each list has that patient's visits only
- EC33: AC12, ownership: a stranger or another family's guardian asks for a patient's visits; empty
- EC34: AC12, size none: a patient with no visits; empty
- EC35: AC12, follow the data: a cancelled visit is still listed, as Mine lists it
- EC36: AC13, two at once: eight patients book the same slot at the same moment; one visit, seven SlotTaken
- EC37: AC13, two at once: one patient books eight slots at the same moment with one code; only one visit carries the code

## Risks

| Area | Likelihood | Harm | Score |
|---|---|---|---|
| Booking for someone who isn't yours (wrong patient) | 2 | 3 | 6 |
| Get / ForPatient shows a visit to the wrong person | 2 | 3 | 6 |
| Two bookings of one slot at once (double booking) | 3 | 3 | 9 |
| Charged amount: price, promo percent, promo used twice (money) | 2 | 3 | 6 |
| Cancel after the start, or by someone else | 2 | 2 | 4 |
| Cancelled slot not freed | 2 | 2 | 4 |
| Note length limit | 1 | 1 | 1 |

## Open questions (not tested until answered)

- Q1: Promo rounding. Price x percent can give fractions of the smallest coin (99.99 at 15% = 84.9915). Round how, to how many places, and which way? Tests use prices that divide exactly.
- Q2: "Once per patient": is the patient the visit's patient or the user who books? Can a guardian use a code once for themselves and once for each dependent? Tests only assume it is counted per PatientId for the same patient twice, and that an unrelated patient is not blocked.
- Q3: Does a code on a visit that was later cancelled count as used? Can it be used again on the rebooking?
- Q4: Is an empty or whitespace-only PromoCode "no code" or an unknown code?
- Q5: May a promo's PercentOff be outside 0..100, and what happens if the store has one?
- Q6: When several refusals apply at once (another's patient AND an unknown promo AND a too-long note), which one is returned? Tests only ever break one rule at a time.
- Q7: Is the note limit 500 UTF-16 chars (string.Length) or 500 visible characters? An emoji is 2 chars; Arabic letters with diacritics are several. Tests use plain BMP letters, where both agree.
- Q8: Who may cancel? The request says when, not who. Inferred: the patient and their guardian. Can a dependent cancel a visit their guardian booked for them? (Tests assume the patient can.)
- Q9: Can a dependent sign in and book for themselves? Tests assume yes (they are a patient).
- Q10: Should ForPatient for someone the caller can't see return an empty list (inferred, nothing leaks) or be an error the API turns into 403/404?
- Q11: What does the receipt do with ShownPrice when it differs from Charged? The desk ignores it for the charge (AC3); is it meant to refuse a stale price instead?
