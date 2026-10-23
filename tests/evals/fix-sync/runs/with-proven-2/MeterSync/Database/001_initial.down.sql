-- 001_initial (down): removes the MeterSync schema, and every reading with it. Only for rolling back a
-- deploy that never went live; on a live database it destroys patient data.
SET lock_timeout = '5s';

BEGIN;

DROP TABLE readings;
DROP TABLE meters;
DROP TABLE patients;

COMMIT;
