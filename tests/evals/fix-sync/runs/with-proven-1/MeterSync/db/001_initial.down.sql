-- MeterSync: first deployment, down. Drops everything 001_initial.up.sql created, children first.
-- This deletes every stored reading: run it only to undo a failed first deployment.
SET lock_timeout = '5s';

BEGIN;

DROP TABLE IF EXISTS readings;
DROP TABLE IF EXISTS meters;
DROP TABLE IF EXISTS patients;

COMMIT;
