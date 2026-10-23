# 2026-10-23: alert delivery, foreign keys, case-insensitive serials

Not yet run against PostgreSQL: there was no database where this change was made. Run it on a copy first.
The DDL matches what the model generates (`SyncDbContextTests` reads it).

Order:
1. `01-up.sql` (one transaction) before the new pods start. Additive: old pods keep working.
2. Deploy. Use a Recreate rollout, or scale the old pods to zero first: an old pod and a new pod sweep
   the same alerts in different ways, and the patient gets two SMS.
3. `02-backfill.sql` after the last old pod is gone (it picks up alerts the old pods stored meanwhile).
4. `03-indexes.sql` outside a transaction, each statement on its own (`CREATE INDEX CONCURRENTLY`).
5. `04-validate.sql`.

`down.sql` undoes all of it; run it only after the old version is back.
