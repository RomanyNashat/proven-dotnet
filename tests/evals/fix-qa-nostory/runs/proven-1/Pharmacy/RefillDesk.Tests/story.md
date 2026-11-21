# RefillDesk: characterization criteria

There is no written story. These are inferred from the request ("patients, and caregivers they have
delegated, request refills; a pharmacist approves or rejects them and then dispenses") and from what
`Pharmacy/RefillDesk/` enforces today. The tests pin the current behaviour; every line stays `(inferred)`
until the team confirms it. Lines marked QUESTION pin behaviour that may be unintended.

"Today" below is the Riyadh date (UTC+3). runsOut = LastDispensedOn + DaysSupply.

## Criteria

AC1: (inferred) A request for a prescription the store doesn't have is refused with NotFound, and nothing is stored.
AC2: (inferred) The patient may request their own prescription; anyone else may only if they hold a delegation from that patient whose From..To (both days included) covers today; otherwise NotYourPrescription.
AC3: (inferred) A prescription can be requested up to and including ExpiresOn; from the day after it is refused with Expired.
AC4: (inferred) A prescription with RefillsLeft of 0 or less is refused with NoRefillsLeft.
AC5: (inferred) A refill can be requested from WindowDays (7) days before runsOut; earlier it is refused with TooEarly.
AC6: (inferred) A prescription that already has a Requested or PendingApproval request is refused with AlreadyOpen; requests in any other status, or on other prescriptions, don't block.
AC7: (inferred) When several refusals apply, the first in this order wins: NotFound, NotYourPrescription, Expired, NoRefillsLeft, TooEarly, AlreadyOpen.
AC8: (inferred) An accepted request is stored and returned with Refusal None: PendingApproval for a controlled medicine, otherwise Requested, with the prescription's id and the requester's id (the caregiver's when a caregiver asks). A refused request returns no request and changes nothing in the store.
AC9: (inferred) Today is the Riyadh date (UTC+3, fixed), whatever the machine's time zone, and it is the date used by every request check and written as LastDispensedOn.
AC10: (inferred) Move allows only Requested->Approved, Requested->Cancelled, PendingApproval->Approved, PendingApproval->Rejected, PendingApproval->Cancelled and Approved->Dispensed; it returns true and changes the status for those, and returns false and changes nothing for any other pair or an unknown request.
AC11: (inferred) Dispensing takes one refill off the prescription and sets LastDispensedOn to today, leaving its other fields alone; no other move touches the prescription.
AC12: (inferred) Requests and moves on one desk are serialized: parallel requests on one prescription accept exactly one, a request is dispensed once, and no refill decrement is lost.
AC13: (inferred) The public contract is stable: PharmacyOffset is 3 hours, WindowDays is 7, the enums keep their members and numeric values, and RefillResult.Ok is true only for None.
AC14: (inferred) InMemoryRefillStore numbers requests 1, 2, 3... across prescriptions, returns null for unknown ids, filters requests by prescription and delegations by caregiver in insertion order, and Add/Save replace a prescription by id.

## Edge cases

EC1: AC2, boundary: a delegation whose From is today is accepted
EC2: AC2, boundary: a delegation whose From is tomorrow (not yet started) is refused with NotYourPrescription
EC3: AC2, boundary: a delegation whose To is today is accepted
EC4: AC2, boundary: a delegation whose To was yesterday (expired) is refused with NotYourPrescription
EC5: AC2, ownership: a caregiver's current delegation for a different patient is refused
EC6: AC2, ownership: another caregiver's delegation for the same patient doesn't let this caregiver in
EC7: AC2, ownership: a stranger with no delegation is refused
EC8: AC2, decision table: a caregiver with several delegations is accepted only when one of them is for this patient and covers today
EC9: AC2, boundary: a delegation with From after To covers no day and is refused
EC10: AC2, decision table: the patient needs no delegation
EC11: AC3, boundary: on ExpiresOn itself the request is accepted
EC12: AC3, boundary: the day after ExpiresOn it is refused with Expired
EC13: AC4, boundary: RefillsLeft 1 is accepted, 0 is refused with NoRefillsLeft
EC14: AC4, boundary: negative RefillsLeft (-1, int.MinValue) is refused with NoRefillsLeft
EC15: AC5, boundary: today exactly runsOut-7 is accepted, for several DaysSupply values
EC16: AC5, boundary: today runsOut-8 (one day earlier) is refused with TooEarly
EC17: AC5, boundary: on or after runsOut (a late refill) is accepted
EC18: AC6, states: an existing Requested or PendingApproval request blocks with AlreadyOpen
EC19: AC6, states: an existing Approved, Rejected, Dispensed or Cancelled request doesn't block (QUESTION for Approved)
EC20: AC6, ownership: an open request on another prescription of the same patient doesn't block
EC21: AC6, double submit: a second request (same patient or their caregiver) after an accepted one is AlreadyOpen
EC22: AC7, order: NotFound wins over a stranger requester and an open request on that id
EC23: AC7, order: NotYourPrescription before Expired
EC24: AC7, order: Expired before NoRefillsLeft
EC25: AC7, order: NoRefillsLeft before TooEarly
EC26: AC7, order: TooEarly before AlreadyOpen
EC27: AC7, order: when every refusal applies, NotYourPrescription is returned
EC28: AC8, decision table: controlled gives PendingApproval, not controlled gives Requested
EC29: AC8, ownership: a caregiver's request records the caregiver's id as RequestedBy, not the patient's
EC30: AC8, persistence: FindRequest returns the same instance that Request returned, and RequestsFor lists it
EC31: AC8, failure: every refusal returns a null request, Ok false, and adds or saves nothing
EC32: AC8, persistence: an accepted request doesn't change or save the prescription
EC33: AC9, clinic date vs UTC midnight: Today is still the same date at 20:59:59.9999999 UTC and the next date at 21:00:00 UTC
EC34: AC9, setting: Today ignores the TimeProvider's local time zone
EC35: AC9, clinic date vs UTC midnight: the expiry check flips at 21:00 UTC
EC36: AC9, clinic date vs UTC midnight: a delegation's From and To flip at 21:00 UTC
EC37: AC9, clinic date vs UTC midnight: the refill window opens at 21:00 UTC
EC38: AC9, clinic date vs UTC midnight: LastDispensedOn written on dispense flips at 21:00 UTC
EC39: AC10, every pair of states: the full 6x6 status table
EC40: AC10, missing: Move on an unknown request id returns false and saves nothing
EC41: AC10, every pair of states: a move to the same status returns false
EC42: AC10, persistence: Move changes the very request object Request returned
EC43: AC11, dispense: RefillsLeft drops by exactly 1, LastDispensedOn becomes today, every other field and every other prescription is unchanged
EC44: AC11, non-dispense: approve, reject and cancel don't save the prescription
EC45: AC11, QUESTION: dispensing checks neither RefillsLeft (0 becomes -1) nor expiry
EC46: AC11, QUESTION: dispensing a request whose prescription is missing throws NullReferenceException after the status has already become Dispensed
EC47: AC11, story: request, approve, dispense; the next request is TooEarly because the window restarted
EC48: AC12, two requests at once: many parallel requests on one prescription produce exactly one accepted request
EC49: AC12, two requests at once: parallel dispenses of one request succeed once and decrement once
EC50: AC12, two requests at once: parallel dispenses of different requests on one prescription lose no decrement
EC51: AC13, constants: PharmacyOffset is exactly 3 hours and WindowDays is 7
EC52: AC13, contract: RefillStatus and RefillRefusal keep their names and numeric values
EC53: AC13, contract: RefillResult.Ok is true only for None, and results compare by value
EC54: AC14, ids: request ids run 1, 2, 3 across prescriptions and keep the status given
EC55: AC14, missing: unknown prescription and request ids return null
EC56: AC14, filter: RequestsFor and DelegationsOf return only their own rows, in insertion order
EC57: AC14, replace: Add and Save replace a prescription with the same id
