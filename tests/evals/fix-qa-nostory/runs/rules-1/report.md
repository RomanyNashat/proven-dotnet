# RefillDesk tests — report

The suite in `Pharmacy/RefillDesk.Tests/` has 135 tests (counting each theory case). All of them pass on the service as it is now, and they passed in three runs in a row. I did not change anything in `Pharmacy/RefillDesk/`.

Because there is no story, the tests record **what the service does today**. Where that behaviour looks questionable (see "Unclear" below), the test still expects the current behaviour, and its name or a comment says so. That way, if someone changes it on purpose, the failing test points at a decision the team needs to make rather than a regression.

## What is tested

**`RequestTests.cs`: `Desk.Request`**
- **Successful requests:**
  - A non-controlled prescription gives a `Requested` request; a controlled one gives `PendingApproval`.
  - `PrescriptionId` and `RequestedBy` are set (for a caregiver, `RequestedBy` is the caregiver's id, not the patient's).
  - The request is stored, and the prescription is left unchanged.
- **NotFound:** an unknown prescription is refused and nothing is stored.
- **Who may ask:**
  - The patient may ask without needing a delegation.
  - A stranger, another patient, a caregiver delegated by a different patient, and a delegation that belongs to someone else are all refused.
  - Delegations only work one way.
  - A delegation's `From` and `To` days both count. One with `From` after `To` is never active.
  - If a caregiver has several delegations, any one active delegation is enough.
  - Delegation dates are judged on the Riyadh date: 20:59:59Z and 21:00Z fall on different days.
- **Expiry:** a request on the expiry day itself is allowed, the day after is `Expired`, and the day is the Riyadh date.
- **Refills left:** 1 is allowed; 0 and negative values give `NoRefillsLeft`.
- **Refill window:**
  - The supply runs out on `LastDispensedOn + DaysSupply`, and requests are allowed from 7 days before that.
  - Tested at runs-out −8, −7, −6, 0, +1 and +100 days.
  - Also tested with different `DaysSupply` values, including 0 and 7.
  - The window uses the Riyadh date, and `WindowDays == 7`.
- **One open request at a time:**
  - A `Requested` or `PendingApproval` request blocks a new one, whether the patient or the caregiver made it.
  - `Rejected`, `Cancelled` and `Approved` requests do not block a new one.
  - After dispensing, the window starts again from the dispense date.
  - An open request on one prescription doesn't block another prescription, and a refused request doesn't count as open.
- **Order of checks:** NotFound → NotYourPrescription → Expired → NoRefillsLeft → TooEarly → AlreadyOpen. Each step is tested against a setup where every later check would also fail.
- The desk reads the current prescription from the store each time, and the clock moving forward is seen.

**`MoveTests.cs`: `Desk.Move`**
- **Full transition table:** every one of the 6×6 status pairs is tested. Exactly these six moves are allowed:
  - Requested → Approved or Cancelled
  - PendingApproval → Approved, Rejected or Cancelled
  - Approved → Dispensed

  For each pair the test checks the return value, the request's status, and whether the prescription changed.
- Unknown request ids and status values outside the enum return `false`.
- **Dispensing:**
  - It uses one refill and sets `LastDispensedOn` to the **Riyadh** date at the moment of dispensing (tested on both sides of midnight). Every other prescription field is kept, and other prescriptions are untouched.
  - Approving does not change the prescription.
  - Dispensing the same request twice uses one refill.
  - Dispensing the last refill leaves 0, and the next request gets `NoRefillsLeft`.
  - Dispensing does not re-check expiry, delegation or `RefillsLeft`.
- End-to-end flows for controlled and non-controlled prescriptions.

**`ConcurrencyTests.cs`:** these use a store wrapper that pauses on reads, so a missing lock would show up as a failure.
- Eight requests at once for the same prescription: exactly one is accepted and the rest get `AlreadyOpen`.
- Eight dispenses at once of the same request: exactly one succeeds and one refill is used.
- Dispensing different requests at once: each one uses a refill.
- Approve and cancel at the same time: exactly one wins.

**`DeskBasicsTests.cs`**
- `Today` is the UTC+3 date, including across the year end, in summer (there is no daylight saving), and when the clock is given in a non-UTC offset.
- `PharmacyOffset` is +3h.
- `RefillResult.Ok` is true only for `None`.
- The members and order of both enums are fixed, so adding, removing or reordering a member is detected.
- `InMemoryRefillStore`:
  - Request ids are sequential starting at 1.
  - Lookups are filtered by prescription and by caregiver.
  - `RequestsFor` returns a snapshot.
  - `Add` and `Save` replace a prescription by id.

### How I checked the tests catch changes
On a throw-away copy of the service, I applied 26 deliberate changes one at a time:
- each boundary flipped (`<`/`<=`, `>`/`>=`)
- the time offset and `WindowDays` changed
- each allowed move removed, and extra moves added
- `Approved` counted as open, and `PendingApproval` not counted as open
- the controlled flag ignored
- the wrong `RequestedBy` recorded
- the refill decrement removed, and the `LastDispensedOn` update removed
- each `lock` removed
- a check removed, and two checks swapped
- the runs-out date off by one
- a `RefillsLeft` check added at dispense time
- request ids starting at 0

The suite failed on every one of them. I also ran the two lock-removal changes a second time, and they were caught again.

## Unclear about the service

1. **A non-controlled request can't be rejected.** The allowed moves are `Requested → Approved/Cancelled`, but `Requested → Rejected` is not allowed. The brief says "a pharmacist approves or rejects", so either non-controlled refills are never meant to be rejected, or this move is missing. Pinned by `Requested_CannotBeRejected_ButCanBeCancelled`.
2. **An approved request doesn't count as "open".** The `AlreadyOpen` check only looks at `Requested` and `PendingApproval`. While one refill is approved but not yet dispensed, the patient can file another one. Both can then be approved and dispensed, and **`RefillsLeft` can go negative**, because `Move` never re-checks it. Pinned by `ApprovedButNotDispensedRequest_DoesNotBlockANewOne` and `Dispense_IsNotRecheckedAgainstRefillsLeft_CanGoNegative`. This looks like a likely bug.
3. **Nothing is re-checked when a request is approved or dispensed.** Expiry, refills and delegation are only checked at request time. A request made on the expiry day, or by a caregiver whose delegation then ends, can still be dispensed later. Is that intended?
4. **An approved request can't be cancelled.** `Approved → Cancelled` and `Approved → Rejected` are not allowed. So if the patient no longer wants a refill after approval, the only way forward is to dispense it.
5. **`Move` doesn't know who is acting.** Any caller can approve, reject, dispense or cancel. It isn't clear whether the patient may cancel, or only the pharmacist, and whether a caregiver may cancel a request.
6. **`Move` depends on the store returning live objects.** It changes the `RefillRequest` it got from the store (`request.Status = to`) and never saves it. That works with `InMemoryRefillStore`, which hands back shared references, but a store that returns copies would silently lose status changes. `IRefillStore` has no method for saving a request.
7. **The lock is per `Desk` instance.** Two desks on the same store aren't kept in step with each other. The guarantees in the concurrency tests only hold for a single desk.
8. **`LastDispensedOn` is the actual dispense date.** After dispensing, the window starts again from the day the medicine was dispensed. It does not continue from the previous runs-out date, so a late pickup moves the schedule later and an early one moves it earlier. That's probably intended, but nothing documents it.
9. **The `Desk` doc comment says "See story.md", but that file doesn't exist.**
10. **Odd data is accepted without complaint.** A delegation with `From > To` is simply never active. Zero or negative `DaysSupply`, and negative `RefillsLeft` (which is treated as "no refills"), are not rejected anywhere.
