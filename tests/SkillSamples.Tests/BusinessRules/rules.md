# Rules: cancelling a visit (`VisitCancellation`)

Mined from the code as it is. Each rule names the line that enforces it, with that line's code so it is
found again when lines move, and each has a test that passes on the current code.

- R1: Given a visit that isn't the patient's, or doesn't exist, when the patient cancels it, then the answer is NotFound and nothing is refunded. (VisitCancellation.cs:25 `visit.PatientId != patientId`)
- R2: Given a visit that is already cancelled, when anyone cancels it again, then the answer is AlreadyCancelled and nothing more is refunded. (VisitCancellation.cs:27 `if (visit.Cancelled)`)
- R3: Given any booked visit, when the clinic cancels it, even inside the cutoff, then it is cancelled and the patient gets back all they paid. (VisitCancellation.cs:29 `if (byClinic)`)
- R4: Given a visit that starts in less than 2 hours, when the patient cancels it, then the answer is TooLate; at exactly 2 hours it may still be cancelled. (VisitCancellation.cs:33 `left < Cutoff`)
- R5: Given a patient who cancelled 2 visits in the last 30 days, when they cancel a third, then the answer is NeedsClinicCall and nothing is refunded; it is only asked once the visit is outside the cutoff. (VisitCancellation.cs:35 `>= CancellationsBeforeCall`)
- R6: Given a patient who cancels in time, when the visit is 24 hours or more away, then all they paid comes back; closer than that, half, rounded down to the smallest unit. (VisitCancellation.cs:38 `var refund =`)

## Questions the rules raise
- R6 rounds the half refund down (12.625 becomes 12.62): is that meant, or should it round to the nearest?

## Not rules
- VisitCancellation.cs:19 `Cutoff =`: the cutoff's value, used by R4.
- VisitCancellation.cs:20 `FullRefundBefore =`: the full-refund window's value, used by R6.
- VisitCancellation.cs:21 `CancellationsBeforeCall =`: the cancellations before a call, used by R5.
