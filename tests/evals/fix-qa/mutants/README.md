# The planted bugs

Each folder replaces `Pharmacy/RefillDesk/Desk.cs` with one business bug. A run's tests catch it when at
least one of them fails on that copy, after all of them passed on the correct one.

| Bug | What is wrong |
|---|---|
| M01 | the pharmacy's day is taken in UTC (AC8) |
| M02 | the first day of the refill window is refused (AC3) |
| M03 | a prescription with no refills left is accepted (AC2) |
| M04 | a caregiver's request for a controlled medicine skips approval (AC5) |
| M05 | a delegation's last day is refused (AC1) |
| M06 | a request waiting for approval doesn't count as open (AC4) |
| M07 | a request waiting for approval can be dispensed (AC6) |
| M08 | refills go down on approval, not on dispensing (AC7) |
| M09 | the expiry date itself is refused (AC2) |
| M10 | two requests at the same moment both open (AC4) |
