# The planted bugs

Each folder replaces `Pharmacy/RefillDesk/Desk.cs` with one business bug. A run's tests catch it when at
least one of them fails on that copy, after all of them passed on the correct one.

| Bug | What is wrong |
|---|---|
| M01 | a delegation's days are taken in UTC (AC1, AC8) |
| M02 | the expiry is checked in UTC (AC2, AC8) |
| M03 | a delegation for another patient counts (AC1) |
| M04 | only a caregiver's first delegation for the patient counts (AC1) |
| M05 | a rejected request still counts as open (AC4) |
| M06 | two dispenses of one request at the same moment both go through (AC6, AC7) |
| M07 | dispensing doesn't start a new supply (AC3) |
| M08 | an approved request still counts as open (AC4) |
