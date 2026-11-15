# Story: request a prescription refill

As a patient, or a caregiver acting for one, I request a refill of a repeat prescription in the app, so the
pharmacy prepares it before the supply runs out.

## Acceptance criteria

- AC1: The patient can request a refill of their own prescription. A caregiver can request one for a patient while they hold a delegation for that patient that is valid on the day of the request; a delegation is valid from its first day to its last day, both included. Anyone else is refused as not their prescription.
- AC2: The prescription must still be valid (the day of the request is on or before its expiry date) and must have refills left.
- AC3: The current supply runs out on the last dispensing date plus the days' supply. A refill can be requested from 7 days before that date onwards; earlier is refused as too early.
- AC4: A prescription has at most one open request at a time. A request is open while it is Requested or PendingApproval; another request for it is refused, even when two arrive at the same moment.
- AC5: A request for a controlled medicine starts as PendingApproval, whoever makes it; any other starts as Requested.
- AC6: A request moves Requested to Approved or Cancelled; PendingApproval to Approved, Rejected or Cancelled; Approved to Dispensed. No other move is allowed.
- AC7: The refills left go down by one when a request is Dispensed, and at no other time.
- AC8: Every day in these rules is a day at the pharmacy, in Riyadh (UTC+3), not in UTC.
