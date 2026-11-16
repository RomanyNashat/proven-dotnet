# Plan: patients cancel a booked visit

| Task | Best | Likely | Worst | Unknowns |
|---|---|---|---|---|
| Cancel endpoint with the ownership check | 3 | 4 | 6 | |
| Refund rules (full a day out, half closer) | 2 | 3 | 5 | |
| Free the slot for the waiting list | 2 | 4 | 14 | WaitingList has three callers we haven't read |
| Text message to the patient in their language | 1 | 2 | 4 | |
| Integration tests on SQL Server | 3 | 5 | 8 | |
