# Queue position: advisory lock per clinic and day

**Date:** 2026-11-14
**Service:** ClinicQueue
**Status:** in-progress

## Decision
`POST /visits` books inside a transaction that first takes `pg_advisory_xact_lock(clinicId, day.DayNumber)`,
then sets the position to `MAX(position) + 1` for that clinic and day. The transaction stays at read
committed.

## Alternatives Considered
- Unique index on `(clinic_id, day, position)` with retry on conflict: the right backstop, but the
  schema can't change in this work. Worth adding later as a reviewed script.
- `SELECT … FROM clinic WHERE id = … FOR UPDATE`: makes bookings wait across every day of a clinic,
  not just the day being booked.
- Serializable transaction with retry: needs retry code around every booking, and fails more often under load.
- Keeping `COUNT + 1`: repeats a position once any visit is deleted.

## Reasoning
The lock needs no schema change, works across pods, and is released at commit or rollback. Read
committed matters: under repeatable read the snapshot is taken before the lock wait ends, so the MAX
would miss the booking that held the lock.
