# RefillDesk: tests for the refill story

There are 168 tests in `Pharmacy/RefillDesk.Tests/`, and all of them pass on the service as it is. Nothing in `RefillDesk/` was changed.

The tests use `InMemoryRefillStore` with a `FakeTimeProvider`. The default "today" is 20 Nov 2026, 12:00 in Riyadh. A shared helper sets this up (`TestPharmacy.cs`). It also has `RequestIn(status)`, which takes a request to any status using only allowed moves.

Each test sets up one failing condition at most. A test that checks one rule never depends on how the service orders its checks.

## Criterion by criterion

**AC1: who may request** (`WhoMayRequestTests`, plus the delegation cases in `PharmacyDayTests`)
- The patient asking for their own prescription is accepted, and the request is on that prescription.
- These are refused as `NotYourPrescription`:
  - a stranger
  - another patient
  - a caregiver with no delegation
  - a caregiver delegated for a different patient
  - a caregiver when the delegation belongs to a different caregiver
- Delegation dates, checked against the day of the request:
  - accepted on the first day, on the last day, on days in between, and when the delegation is a single day
  - refused the day before it starts and the day after it ends
- A caregiver with several delegations:
  - a lapsed one and a current one for the patient: accepted
  - a lapsed one for the patient and a current one for someone else: refused
- A refused request creates no request record. A prescription id that doesn't exist is refused (the reason isn't checked, see below).

**AC2: still valid, with refills left** (`PrescriptionValidityTests`)
- Expiry: accepted on the expiry day and before it; refused as `Expired` the day after and later. This holds for a caregiver too.
- Refills: accepted with 1 or more left; refused as `NoRefillsLeft` with 0 (or a negative number).

**AC3: the refill window** (`RefillWindowTests`)
- Run-out date = last dispensing date + days' supply. I used several supplies (7, 10, 28, 30, 90 days) so a hard-coded value or a wrong formula fails.
- Accepted on run-out − 7 (the first day), run-out − 6, the run-out day itself, and after it.
- Refused as `TooEarly` on run-out − 8 and earlier.
- Two prescriptions on the same day whose last dispensing dates differ by one day: one is accepted, the other is too early.

**AC4: at most one open request** (`OpenRequestTests`)
- While a request is `Requested` or `PendingApproval`, a second one is refused as `AlreadyOpen`, whether the patient or the caregiver asks.
- After `Approved`, `Rejected`, `Cancelled` or `Dispensed`, a new request is accepted. Those statuses aren't open.
- An open request on one prescription doesn't block another prescription.
- **Same moment:** 4 threads, released together by a barrier, ask for the same prescription. Exactly one is accepted, the others get `AlreadyOpen`, and the store holds one request. The test store pauses 150 ms inside the open-request lookup. Without locking around check-then-add, every caller would see "nothing open" and all of them would be accepted. So the test fails reliably when the lock is removed, and it isn't flaky while the lock is there.

**AC5: starting status** (`InitialStatusTests`)
- A controlled medicine starts as `PendingApproval`, whether the patient or the caregiver asks. Any other medicine starts as `Requested`, for both.

**AC6: status moves** (`StatusMoveTests`)
- I tried all 36 from/to pairs:
  - the 6 allowed moves return `true` and set the new status
  - the other 30 (including moving to the same status, and every move out of a final status) return `false` and leave the status as it was
- Moving an unknown request returns `false`. Moving one request doesn't change another.

**AC7: refills left** (`RefillsLeftTests`)
- `Dispensed` lowers refills left by exactly 1 (tested from 3 and from 1).
- Dispensing the last refill means the next request is refused as `NoRefillsLeft`.
- Refills left don't change on:
  - a request, accepted (controlled or not) or refused
  - Approved, Cancelled or Rejected (all five allowed moves other than dispensing)
  - a refused dispense: skipping approval, from Rejected or Cancelled, or dispensing twice
  - a dispense on another prescription
- After dispensing, the prescription equals the original with only `RefillsLeft − 1` and `LastDispensedOn = today` changed. A follow-up test checks that this starts a new window (see "Unclear" below).

**AC8: Riyadh days** (`PharmacyDayTests`)
- `Desk.Today` is the Riyadh date. For 20 Nov, 20:59:59 UTC is still 20 Nov in Riyadh and 21:00:00 UTC is already 21 Nov. These two instants are next to each other, so a UTC date, local machine time, or any offset other than +3 fails. Also checked: midnight UTC, the turn of the year, and an input given in a different offset.
- Each day rule is tested at those two instants:
  - the expiry day
  - the first day of the window
  - the first and last day of a delegation
  - the date recorded as the last dispensing date

## Unclear in the story

1. **Which refusal wins when several rules fail** (for example a stranger asking for an expired prescription). The story doesn't say. Today, not-your-prescription is checked first, so a stranger learns nothing about someone else's prescription. That is probably worth keeping, but it isn't a criterion, so no test pins the order.
2. **Unknown prescription.** The story doesn't mention it. The tests only check that it's refused, not that the reason is `NotFound`.
3. **Refusal reasons for AC2.** The story says "must be valid / must have refills" without naming the refusal. The tests assert the service's `Expired` and `NoRefillsLeft` values, so renaming or swapping them fails a test.
4. **Dispensing updates the last dispensing date.** AC3 uses "the last dispensing date", but no criterion says dispensing changes it. The service sets it to the Riyadh day of the dispense, and I tested that because AC3 doesn't work otherwise. Please confirm that's what you mean.
5. **`Approved` isn't open, so a second request is allowed while one is approved but not yet dispensed.** That's the literal reading of AC4, and it's tested that way. The result: with 1 refill left, a patient can request → approve → request again → approve, and both can be dispensed, so refills left goes to −1. Nothing in the story stops dispensing with 0 refills, or after expiry. This looks like a real gap in the story, not in the code.
6. **"Two at the same moment" only holds inside one `Desk`.** The lock is per `Desk` instance, and the tests use one instance. Two `Desk` instances on the same store, or several pods on a real database, would not be protected. The story doesn't say what the deployment looks like, so I didn't test it.
7. **Who may move a request** (approve, reject, cancel, dispense) isn't in the story. `Move` takes no caller and checks no ownership, so anyone who knows a request id can cancel it.
8. **A delegation whose start date is after its end date** isn't covered by the story. It's never valid today, and it isn't tested.

## Not done

I wrote a mutation script (`/tmp/mut/run.sh`) to confirm that each test fails when the service breaks. It runs against a scratch copy of the service: no lock, `<` instead of `<=` on each boundary, a window of 6 or 8 days, the UTC date, a +4 offset, an extra or missing move, decrementing by 2, and others. Running it needs a shell command you hadn't approved, so it didn't run. I checked each break by reading through what the tests would do instead, but that is not proof. Approve `bash /tmp/mut/run.sh` if you want it confirmed.
