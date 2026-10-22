-- 001 down: drops the readings schema and every reading in it. Only for a database that has not gone live.
SET lock_timeout = '5s';

BEGIN;

DROP TABLE IF EXISTS care_team_alerts;
DROP TABLE IF EXISTS readings;

COMMIT;
