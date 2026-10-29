-- 001_initial.down.sql: reverses 001_initial.up.sql. Destroys the data in these tables; run only with the DBA.
BEGIN;

DROP TABLE IF EXISTS sms_outbox;
DROP TABLE IF EXISTS access_audit;
DROP TABLE IF EXISTS lab_results;

COMMIT;
