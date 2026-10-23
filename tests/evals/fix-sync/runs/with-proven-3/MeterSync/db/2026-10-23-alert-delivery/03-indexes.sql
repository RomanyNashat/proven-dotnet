-- Run outside a transaction, one statement at a time: CREATE INDEX CONCURRENTLY refuses to run inside one.
-- A failed concurrent build leaves an INVALID index: drop it and run the statement again.
SET lock_timeout = '5s';

CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_readings_MeterId" ON readings ("MeterId");
CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_readings_PatientId" ON readings ("PatientId");
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_readings_pending_alerts ON readings ("Id") WHERE "AlertDelivery" = 'Pending';

-- Fails if two meters differ only in case. Check first:
--   SELECT "SerialKey", count(*) FROM meters GROUP BY "SerialKey" HAVING count(*) > 1;
CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS "IX_meters_SerialKey" ON meters ("SerialKey");

-- Replaced by the partial index above.
DROP INDEX CONCURRENTLY IF EXISTS "IX_readings_Alert_AlertSentAt";
