# RefillDesk tests: report

## Result

There are 176 tests in `Pharmacy/RefillDesk.Tests/`, and all of them pass on the service as it is now. `dotnet test Pharmacy/RefillDesk.Tests` passed 176 of 176 on every run: three of my own and four by the qa-engineer agent. Nothing in `Pharmacy/RefillDesk/` was changed; only the build's `bin/` and `obj/` folders appeared there.

The qa-engineer agent designed and wrote the tests. There is no story, so it worked out the criteria from the code. They are listed as inferred (AC1–AC14, EC1–EC57) in `Pharmacy/RefillDesk.Tests/story.md`. I read its report, the test helpers and the move and concurrency tests, and reran the suite.

**Do they catch changes?** Stryker couldn't run here, because `dotnet tool` is blocked. Instead, the agent copied the service, made 52 separate behaviour changes to the copy, and ran the real tests against each one. Every change made at least one test fail. One weak spot turned up this way and was fixed: a helper used `Desk.WindowDays` where it now uses a literal 7. Two of the changes remove the locks. Catching those depends on timing, but the tests caught them on every run.

## What is tested

| Area | What is pinned |
|---|---|
| Refusals | Each refusal value: NotFound, NotYourPrescription, Expired, NoRefillsLeft, TooEarly, AlreadyOpen. Also the order they are checked in, shown by one test for each pair of neighbours plus one where all apply. A refused request returns no request and doesn't write to the store. |
| Who may ask | The patient needs no delegation. A caregiver's delegation counts on both its From and To days, but not the day before or after. These are refused: a delegation for a different patient, another caregiver's delegation, a stranger, and a delegation whose From is after its To. With several delegations, one valid one is enough. |
| Expiry | A request on ExpiresOn is accepted; from the day after, it is Expired. |
| Refills left | 1 is accepted; 0, −1 and int.MinValue are refused. |
| Refill window | The window opens 7 days before LastDispensedOn + DaysSupply. On the opening day the request is accepted; the day before, it is TooEarly. Checked for several supply lengths, including 0 and 1 days. |
| Riyadh date (UTC+3) | 20:59:59.9999999 UTC and 21:00 UTC fall on different Riyadh dates. This is checked for Today, expiry, delegation start and end, the window opening, and the LastDispensedOn written on dispense. The time provider's local time zone makes no difference. |
| Open requests | A Requested or PendingApproval request blocks a new one. Approved, Rejected, Dispensed and Cancelled requests don't, and neither does a request on a different prescription. |
| Accepted request | Controlled prescriptions start at PendingApproval, others at Requested. The request's Id, PrescriptionId and RequestedBy are checked; RequestedBy is the caregiver's id when a caregiver asks. The request is stored and `FindRequest` returns that same object. |
| Status moves | All 36 from/to pairs. Exactly 6 moves are allowed; the rest return false and leave the status alone, including a move to the same status. An unknown request id returns false. |
| Dispensing | RefillsLeft goes down by exactly 1 and LastDispensedOn becomes today's Riyadh date. The whole prescription record is compared, and other prescriptions are untouched. Other moves never save the prescription. |
| Concurrency | 32 threads requesting at once get exactly one accepted request. 32 dispenses of the same request succeed once. Dispenses of 32 different requests at once lose no decrement. |
| Public shape | `PharmacyOffset` is 3h and `WindowDays` is 7. The enum names and their numeric values, `Ok`, and record equality. |
| In-memory store | Ids are 1, 2, 3, …; unknown ids return null; lookups filter and keep insertion order; Add and Save replace an existing record. |

## What I found unclear in the service

The tests pin each of these as the service does it now. If one of them is a bug, fixing it will make the test named here fail, and that test should be updated on purpose as part of the fix.

1. **A non-controlled refill can't be rejected.** From Requested it can only be Approved or Cancelled; Rejected is allowed only from PendingApproval. Test: `Move_RequestedNonControlledToRejected_ReturnsFalse`.
2. **An Approved request doesn't block a new one.** A second request can be made, approved and dispensed before the first is dispensed, which uses two refills. Test: `Lifecycle_TwoApprovedRequests_BothDispenseAndTakeTwoRefills`.
3. **Dispensing doesn't check refills or expiry again.** RefillsLeft can drop below zero (0 becomes −1). A request approved on the expiry day can still be dispensed after the prescription has expired.
4. **Dispensing when the prescription is missing** throws a `NullReferenceException`. By then the request's status has already changed to Dispensed, and nothing is saved, so the request is left in an inconsistent state.
5. **Anyone can call `Move`.** It takes no caller, so nothing stops a patient or caregiver from approving or dispensing a request. If the pharmacist-only rule lives somewhere else, it isn't in this service.
6. **Approved can't be cancelled**, and Rejected, Cancelled and Dispensed are final. Is that intended?
7. **Each `Desk` object has its own lock.** Two `Desk` instances on one store, such as a scoped service or more than one server, don't block each other, and the store has no guard against duplicates, so two open requests are possible. This isn't tested because the result depends on timing.
8. **`Move` never saves the request.** It changes the request object's status and relies on the in-memory store handing out the same object it holds. A database-backed store would lose every status change.
9. **NotFound is checked before NotYourPrescription.** A stranger can therefore tell which prescription ids exist.
10. **Bad data is accepted without an error.** Examples: a DaysSupply of 0 or less, a LastDispensedOn in the future, or a delegation whose From is after its To (it simply covers no days). A huge DaysSupply would make the date arithmetic throw; that case isn't tested.
11. The comment on `Desk` says "See story.md", but no story exists. The inferred one in `RefillDesk.Tests/story.md` is not that document.

## Housekeeping

- The agent left a scratch project, `Pharmacy/RefillDesk.Mutation.Tests/`, which holds a copy of the service with the mutation switches. There is also a `Pharmacy/RefillDesk.Tests/TestResults/` folder. Deleting them needed permission I didn't have here, so please remove both. Neither is part of the test project.
- Test files: `DeskTests.cs` (the original test, kept), `RequestTests.cs`, `PharmacyDateTests.cs`, `MoveTests.cs`, `ConcurrencyTests.cs`, `ContractTests.cs`, and the shared helpers in `TestSupport.cs`.
